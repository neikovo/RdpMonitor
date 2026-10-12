using System.Text;

namespace RdpMonitor.Server.Services;

public static class CsvExport
{
    /// <summary>
    /// Escapes one CSV field. Values come from monitored machines (user names, command lines), so a cell that would be
    /// read as a formula by Excel/LibreOffice (starts with = + - @ or a control character) is prefixed with an apostrophe.
    /// </summary>
    public static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;

        var mustQuote = value.AsSpan().IndexOfAny(",\"\r\n") >= 0;
        return mustQuote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static void Row(StringBuilder sb, params string?[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Field(fields[i]));
        }
        sb.Append("\r\n");
    }
}
