using System.Text.Json.Serialization;

namespace Integration.Aws.Sqs;

// Generated from JSON

public record SqsShopEventDetail(
    [property: JsonPropertyName("payload")] SqsShopEventProduct Payload,
    [property: JsonPropertyName("metadata")] SqsShopEventMetadata Metadata
);

public record SqsShopEventProductMessage(
    [property: JsonPropertyName("version")]
    string Version,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("detail-type")]
    string DetailType,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("account")]
    string Account,
    [property: JsonPropertyName("time")] DateTime? Time,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("resources")]
    IReadOnlyList<object> Resources,
    [property: JsonPropertyName("detail")] SqsShopEventDetail Detail
)
{
    public string ToShortString()
    {
        return $"""
                Payload topic: {Detail.Metadata.Topic},
                Item ID: {Detail.Payload.Id},
                Item title: {Detail.Payload.Title}
                """;
    }
}

    public record SqsShopEventMetadata(
        [property: JsonPropertyName("Content-Type")] string ContentType,
        [property: JsonPropertyName("X-Shopify-Topic")] string Topic,
        [property: JsonPropertyName("X-Shopify-Shop-Domain")] string ShopDomain,
        [property: JsonPropertyName("X-Shopify-Product-Id")] string ProductId,
        [property: JsonPropertyName("X-Shopify-Hmac-SHA256")] string HmacSHA256,
        [property: JsonPropertyName("X-Shopify-Webhook-Id")] string WebhookId,
        [property: JsonPropertyName("X-Shopify-API-Version")] string ApiVersion,
        [property: JsonPropertyName("X-Shopify-Event-Id")] string EventId,
        [property: JsonPropertyName("X-Shopify-Triggered-At")] string TriggeredAt
    );

    public record SqsShopEventProduct(
        [property: JsonPropertyName("admin_graphql_api_id")] string AdminGraphqlApiId,
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("variants")] IReadOnlyList<SqsShopEventVariant> Variants
    );

    public record SqsShopEventVariant(
        [property: JsonPropertyName("admin_graphql_api_id")] string AdminGraphqlApiId,
        [property: JsonPropertyName("barcode")] string Barcode,
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("product_id")] long ProductId,
        [property: JsonPropertyName("sku")] string Sku,
        [property: JsonPropertyName("title")] string Title
    );


