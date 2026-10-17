using System.Globalization;
using System.Security.Cryptography;
using TenantPlatform.Core.Services;
namespace TenantPlatform.Application.ServiceRequests;

internal static class ServiceRequestRules
{
    public static List<CreateServiceRequestFieldValueRequest>
        ValidateAndNormalizeValues(
            IReadOnlyCollection<ServiceDefinitionField> fields,
            IReadOnlyCollection<CreateServiceRequestFieldValueRequest>
                submittedValues)
    {
        var submittedByField =
            submittedValues
                .GroupBy(x => x.ServiceDefinitionFieldId)
                .ToDictionary(
                    x => x.Key,
                    x => x.Last());

        var result =
            new List<CreateServiceRequestFieldValueRequest>();

        foreach (var field in fields)
        {
            submittedByField.TryGetValue(
                field.Id,
                out var submitted);

            var value =
                NormalizeValue(
                    field.FieldType,
                    submitted?.Value);

            if (field.IsRequired &&
                string.IsNullOrWhiteSpace(value))
            {
                throw new ServiceRequestValidationException(
                    "ServiceRequestRequiredFieldMissing");
            }

            if (value is null)
            {
                continue;
            }

            result.Add(
                new CreateServiceRequestFieldValueRequest
                {
                    ServiceDefinitionFieldId = field.Id,
                    Value = value
                });
        }

        return result;
    }

    public static string? NormalizeValue(
        ServiceFieldType fieldType,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();

        switch (fieldType)
        {
            case ServiceFieldType.Integer:
                if (!int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var integer))
                {
                    throw new ServiceRequestValidationException(
                        "ServiceRequestInvalidInteger");
                }

                return integer.ToString(
                    CultureInfo.InvariantCulture);

            case ServiceFieldType.Decimal:
                if (!decimal.TryParse(
                    value,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var decimalValue))
                {
                    throw new ServiceRequestValidationException(
                        "ServiceRequestInvalidDecimal");
                }

                return decimalValue.ToString(
                    CultureInfo.InvariantCulture);

            case ServiceFieldType.Boolean:
                if (!bool.TryParse(
                    value,
                    out var booleanValue))
                {
                    throw new ServiceRequestValidationException(
                        "ServiceRequestInvalidBoolean");
                }

                return booleanValue
                    ? "true"
                    : "false";

            case ServiceFieldType.Date:
                if (!DateOnly.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
                {
                    throw new ServiceRequestValidationException(
                        "ServiceRequestInvalidDate");
                }

                return date.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);

            case ServiceFieldType.Email:
                if (!value.Contains('@'))
                {
                    throw new ServiceRequestValidationException(
                        "ServiceRequestInvalidEmail");
                }

                return value;

            default:
                return value;
        }
    }

    public static string CreateReplyToken()
    {
        return Convert.ToHexString(
                RandomNumberGenerator.GetBytes(16))
            .ToLowerInvariant();
    }

    public static bool IsClosedStatus(
        ServiceRequestStatus status)
    {
        return status is
            ServiceRequestStatus.Completed or
            ServiceRequestStatus.Rejected or
            ServiceRequestStatus.Cancelled or
            ServiceRequestStatus.Failed;
    }

    public static string CreateReplySubject(
        string? subject)
    {
        subject = subject?.Trim();

        if (string.IsNullOrWhiteSpace(subject))
        {
            return "Re:";
        }

        if (subject.StartsWith(
            "Re:",
            StringComparison.OrdinalIgnoreCase))
        {
            return subject;
        }

        return $"Re: {subject}";
    }

    public static string CreateReplyBody(
        string reply,
        ServiceRequestMessage originalMessage)
    {
        var sent =
            originalMessage.CreatedAt
                .ToLocalTime()
                .ToString("g");

        return $"""
{reply.Trim()}

-----Original Message-----
From: {originalMessage.FromAddress}
Sent: {sent}
To: {originalMessage.ToAddress}
Subject: {originalMessage.Subject}

{originalMessage.Body}
""";
    }

    public static string? BuildReferences(
        string? existingReferences,
        string? messageId)
    {
        var references =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(
            existingReferences))
        {
            references.AddRange(
                existingReferences
                    .Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries));
        }

        if (!string.IsNullOrWhiteSpace(
            messageId))
        {
            var normalizedMessageId =
                NormalizeMessageId(messageId);

            if (!references.Any(x =>
                string.Equals(
                    NormalizeMessageId(x),
                    normalizedMessageId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                references.Add(
                    normalizedMessageId);
            }
        }

        return references.Count == 0
            ? null
            : string.Join(" ", references);
    }

    public static string NormalizeMessageId(
        string messageId)
    {
        messageId = messageId.Trim();

        if (messageId.StartsWith("<") &&
            messageId.EndsWith(">"))
        {
            return messageId;
        }

        return $"<{messageId}>";
    }

}
