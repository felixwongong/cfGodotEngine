using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;

namespace cfGodotEngine.GoogleSheets;

public interface IGoogleSheetValuesClient
{
    Task<IReadOnlyList<IReadOnlyList<string>>> GetValuesAsync(
        string spreadsheetId,
        string range,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetSheetNamesAsync(
        string spreadsheetId,
        CancellationToken cancellationToken);
}

public sealed class GoogleSheetValuesHttpClient : IGoogleSheetValuesClient, IDisposable
{
    private const string SheetsReadonlyScope = "https://www.googleapis.com/auth/spreadsheets.readonly";
    private readonly GoogleCredential _credential;
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private bool _isDisposed;

    public GoogleSheetValuesHttpClient(
        GoogleCredential credential,
        HttpClient httpClient = null,
        bool disposeHttpClient = true)
    {
        _credential = credential?.CreateScoped(SheetsReadonlyScope)
            ?? throw new ArgumentNullException(nameof(credential));
        _httpClient = httpClient ?? new HttpClient();
        _disposeHttpClient = disposeHttpClient;
    }

    public static GoogleSheetValuesHttpClient CreateFromApplicationDefault()
    {
        var credential = GoogleCredential.GetApplicationDefault()
            .CreateScoped(SheetsReadonlyScope);
        return new GoogleSheetValuesHttpClient(credential);
    }

    public static GoogleSheetValuesHttpClient CreateFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Credential file path cannot be empty.", nameof(path));

        var credential = GoogleCredential.FromFile(path)
            .CreateScoped(SheetsReadonlyScope);
        return new GoogleSheetValuesHttpClient(credential);
    }

    public async Task<IReadOnlyList<IReadOnlyList<string>>> GetValuesAsync(
        string spreadsheetId,
        string range,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_credential.UnderlyingCredential is not ITokenAccess tokenAccess)
            throw new InvalidOperationException("Google credential cannot provide access tokens for Sheets values import.");

        var token = await tokenAccess.GetAccessTokenForRequestAsync(null, cancellationToken)
            .ConfigureAwait(false);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildValuesUri(spreadsheetId, range));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var message = $"Google Sheets values request failed ({(int)response.StatusCode}): {content}";
            if (content.Contains("Unable to parse range", StringComparison.OrdinalIgnoreCase))
            {
                message += $"\nHint: the sheet/tab name in the range '{range}' may not exist or may be misspelled. " +
                           "Open the spreadsheet and verify the exact tab name, or omit sheetName to use the first sheet.";
            }
            throw new HttpRequestException(message);
        }

        return ParseValues(content);
    }

    public async Task<IReadOnlyList<string>> GetSheetNamesAsync(
        string spreadsheetId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_credential.UnderlyingCredential is not ITokenAccess tokenAccess)
            throw new InvalidOperationException("Google credential cannot provide access tokens for Sheets values import.");

        var token = await tokenAccess.GetAccessTokenForRequestAsync(null, cancellationToken)
            .ConfigureAwait(false);
        var encodedSpreadsheetId = Uri.EscapeDataString(spreadsheetId);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"https://sheets.googleapis.com/v4/spreadsheets/{encodedSpreadsheetId}?fields=sheets.properties.title"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Google Sheets metadata request failed ({(int)response.StatusCode}): {content}");

        return ParseSheetNames(content);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        if (_disposeHttpClient)
            _httpClient.Dispose();

        _isDisposed = true;
    }

    private static Uri BuildValuesUri(string spreadsheetId, string range)
    {
        var encodedSpreadsheetId = Uri.EscapeDataString(spreadsheetId);
        var encodedRange = Uri.EscapeDataString(range);
        return new Uri(
            $"https://sheets.googleapis.com/v4/spreadsheets/{encodedSpreadsheetId}/values/{encodedRange}" +
            "?valueRenderOption=FORMATTED_VALUE&dateTimeRenderOption=FORMATTED_STRING");
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseValues(string content)
    {
        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("values", out var valuesElement) ||
            valuesElement.ValueKind != JsonValueKind.Array)
            return Array.Empty<IReadOnlyList<string>>();

        var rows = new List<IReadOnlyList<string>>();
        foreach (var rowElement in valuesElement.EnumerateArray())
        {
            if (rowElement.ValueKind != JsonValueKind.Array)
                continue;

            var row = new List<string>();
            foreach (var cellElement in rowElement.EnumerateArray())
            {
                row.Add(cellElement.ValueKind == JsonValueKind.String
                    ? cellElement.GetString() ?? string.Empty
                    : cellElement.ToString());
            }

            rows.Add(row);
        }

        return rows;
    }

    private static IReadOnlyList<string> ParseSheetNames(string content)
    {
        using var document = JsonDocument.Parse(content);
        var names = new List<string>();
        if (!document.RootElement.TryGetProperty("sheets", out var sheetsElement) ||
            sheetsElement.ValueKind != JsonValueKind.Array)
            return names;

        foreach (var sheet in sheetsElement.EnumerateArray())
        {
            if (sheet.TryGetProperty("properties", out var properties) &&
                properties.TryGetProperty("title", out var title))
            {
                names.Add(title.GetString() ?? string.Empty);
            }
        }

        return names;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(GoogleSheetValuesHttpClient));
    }
}
