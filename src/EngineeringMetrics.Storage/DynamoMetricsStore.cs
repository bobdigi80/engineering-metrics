using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using EngineeringMetrics.Core;

namespace EngineeringMetrics.Storage;

/// <summary>
/// Single-table DynamoDB store. Each snapshot is written twice:
/// <list type="bullet">
/// <item><c>pk = REPO#{owner/name}</c>, <c>sk = {generatedAt}</c>: history, one item per run.</item>
/// <item><c>pk = LATEST</c>, <c>sk = {owner/name}</c>: overwritten each run, so the API reads all repositories with one query.</item>
/// </list>
/// The snapshot itself is stored as JSON in <c>data</c>; nothing queries on its fields.
/// </summary>
public sealed class DynamoMetricsStore(IAmazonDynamoDB dynamo, string tableName) : IMetricsStore, IMetricsReader
{
    private const string LatestPartition = "LATEST";

    public async Task SaveAsync(MetricsSnapshot snapshot, CancellationToken cancellationToken)
    {
        var data = new AttributeValue { S = JsonSerializer.Serialize(snapshot, JsonSerializerOptions.Web) };

        await dynamo.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                Put($"REPO#{snapshot.Repository}", snapshot.GeneratedAt.ToString("O"), data),
                Put(LatestPartition, snapshot.Repository, data),
            ],
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<MetricsSnapshot>> GetLatestAsync(CancellationToken cancellationToken)
    {
        var snapshots = new List<MetricsSnapshot>();
        Dictionary<string, AttributeValue>? startKey = null;

        do
        {
            var response = await dynamo.QueryAsync(new QueryRequest
            {
                TableName = tableName,
                KeyConditionExpression = "pk = :pk",
                ExpressionAttributeValues = new() { [":pk"] = new AttributeValue { S = LatestPartition } },
                ExclusiveStartKey = startKey,
            }, cancellationToken);

            foreach (var item in response.Items ?? [])
            {
                if (item.TryGetValue("data", out var data) && data.S is not null)
                {
                    snapshots.Add(JsonSerializer.Deserialize<MetricsSnapshot>(data.S, JsonSerializerOptions.Web)!);
                }
            }

            startKey = response.LastEvaluatedKey is { Count: > 0 } key ? key : null;
        }
        while (startKey is not null);

        return snapshots;
    }

    private TransactWriteItem Put(string pk, string sk, AttributeValue data) => new()
    {
        Put = new Put
        {
            TableName = tableName,
            Item = new()
            {
                ["pk"] = new AttributeValue { S = pk },
                ["sk"] = new AttributeValue { S = sk },
                ["data"] = data,
            },
        },
    };
}
