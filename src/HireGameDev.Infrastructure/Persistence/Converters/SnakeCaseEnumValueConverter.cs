using System.Collections.Frozen;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HireGameDev.Infrastructure.Persistence.Converters;

/// <summary>
/// Maps PascalCase C# enums (e.g., AccountType.Candidate, JobStatus.PendingReview)
/// to snake_case PostgreSQL text values ('candidate', 'pending_review').
/// </summary>
public sealed class SnakeCaseEnumValueConverter<TEnum> : ValueConverter<TEnum, string>
    where TEnum : struct, Enum
{
    private static readonly FrozenDictionary<TEnum, string> ToDatabaseMap = BuildToDatabaseMap();
    private static readonly FrozenDictionary<string, TEnum> FromDatabaseMap = BuildFromDatabaseMap();

    public SnakeCaseEnumValueConverter()
        : base(
            enumValue => ToSnakeCaseValue(enumValue),
            dbValue => FromSnakeCaseValue(dbValue))
    {
    }

    public static string ToSnakeCaseValue(TEnum value)
    {
        if (ToDatabaseMap.TryGetValue(value, out var mapped))
        {
            return mapped;
        }

        throw new ArgumentOutOfRangeException(
            nameof(value),
            value,
            $"Unsupported {typeof(TEnum).Name} enum value '{value}'.");
    }

    public static TEnum FromSnakeCaseValue(string dbValue)
    {
        if (FromDatabaseMap.TryGetValue(dbValue, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Cannot convert database value '{dbValue}' to enum {typeof(TEnum).Name}.");
    }

    private static FrozenDictionary<TEnum, string> BuildToDatabaseMap()
    {
        var values = Enum.GetValues<TEnum>();
        var dict = new Dictionary<TEnum, string>(values.Length);

        foreach (var item in values)
        {
            dict[item] = ConvertPascalToSnakeCase(item.ToString());
        }

        return dict.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, TEnum> BuildFromDatabaseMap()
    {
        var dict = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in ToDatabaseMap)
        {
            dict[pair.Value] = pair.Key;
        }

        return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static string ConvertPascalToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var builder = new StringBuilder(input.Length + 4);

        for (var i = 0; i < input.Length; i++)
        {
            var current = input[i];
            if (char.IsUpper(current))
            {
                if (i > 0 && (char.IsLower(input[i - 1]) || char.IsDigit(input[i - 1])))
                {
                    builder.Append('_');
                }
                else if (i > 0 && i + 1 < input.Length && char.IsUpper(input[i - 1]) && char.IsLower(input[i + 1]))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
