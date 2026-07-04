using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using CofyDev.Xml.Doc;

namespace cfGodotEngine.GoogleSheets;

public static class GoogleSheetUtil
{
    public static DataTable ToDataTable(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var table = new RawSheetTable(string.Empty, rows);
        var data = SheetTableConverter.ToDataTable(table);

        // Sheets allows JSON arrays inside a single cell (e.g., ["(farm_mine_1,5)"]).
        // The shared converter treats duplicate headers as lists; post-process
        // remaining string values so single-column JSON arrays also become lists.
        foreach (var dataRow in data)
        {
            var keys = dataRow.Keys.ToArray();
            foreach (var key in keys)
            {
                if (dataRow[key] is string value)
                {
                    dataRow[key] = ParseCellValue(value);
                }
                else if (dataRow[key] is string[] values)
                {
                    for (var i = 0; i < values.Length; i++)
                        values[i] = (string)ParseCellValue(values[i]);
                }
            }
        }

        return data;
    }

    public static string SerializeRows(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows == null || rows.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (rowIndex > 0)
                builder.Append("\r\n");

            var row = rows[rowIndex] ?? Array.Empty<string>();
            for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
            {
                if (columnIndex > 0)
                    builder.Append(',');

                builder.Append(EscapeCell(row[columnIndex] ?? string.Empty));
            }
        }

        return builder.ToString();
    }

    public static IReadOnlyList<IReadOnlyList<string>> ParseRows(string content)
    {
        var rows = new List<IReadOnlyList<string>>();
        if (string.IsNullOrEmpty(content))
            return rows;

        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var hasCellContent = false;

        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < content.Length && content[index + 1] == '"')
                    {
                        cell.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cell.Append(character);
                }

                continue;
            }

            if (character == '"' && cell.Length == 0)
            {
                inQuotes = true;
                hasCellContent = true;
            }
            else if (character == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
                hasCellContent = true;
            }
            else if (character == '\r' || character == '\n')
            {
                if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                    index++;

                row.Add(cell.ToString());
                rows.Add(row);
                row = new List<string>();
                cell.Clear();
                hasCellContent = false;
            }
            else
            {
                cell.Append(character);
                hasCellContent = true;
            }
        }

        if (inQuotes)
            throw new FormatException("CSV content ended inside a quoted cell.");

        if (hasCellContent || cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private static object ParseCellValue(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("[", StringComparison.Ordinal) ||
            !trimmed.EndsWith("]", StringComparison.Ordinal))
            return value;

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return value;

            return document.RootElement
                .EnumerateArray()
                .Select(static element => element.ValueKind == JsonValueKind.String
                    ? element.GetString() ?? string.Empty
                    : element.ToString())
                .ToArray();
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static string EscapeCell(string value)
    {
        if (!RequiresQuotes(value))
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static bool RequiresQuotes(string value)
    {
        return value.Contains(',') ||
               value.Contains('"') ||
               value.Contains('\r') ||
               value.Contains('\n') ||
               value.Length != value.Trim().Length;
    }
}
