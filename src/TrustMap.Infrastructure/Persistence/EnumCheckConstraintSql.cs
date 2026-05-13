using System.Globalization;

namespace TrustMap.Infrastructure.Persistence;

internal static class EnumCheckConstraintSql
{
    public static string IntegerValues<TEnum>(string columnName, bool allowNull = false)
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>()
            .Select(value => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));

        return BuildInClause(columnName, values, allowNull);
    }

    public static string StringNames<TEnum>(string columnName, bool allowNull = false)
        where TEnum : struct, Enum
    {
        var values = Enum.GetNames<TEnum>()
            .Select(value => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'");

        return BuildInClause(columnName, values, allowNull);
    }

    private static string BuildInClause(string columnName, IEnumerable<string> values, bool allowNull)
    {
        var quotedColumnName = $"\"{columnName.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        var inClause = $"{quotedColumnName} IN ({string.Join(", ", values)})";

        return allowNull
            ? $"{quotedColumnName} IS NULL OR {inClause}"
            : inClause;
    }
}
