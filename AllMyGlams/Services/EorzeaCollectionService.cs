using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace AllMyGlams.Services;

public sealed record EorzeaImportResult(
    bool Success,
    OutfitRecord? Outfit,
    string Message,
    IReadOnlyList<string> Warnings);

public sealed class EorzeaCollectionService : IDisposable
{
    private const string BaseUrl = "https://ffxiv.eorzeacollection.com";
    private static readonly Regex GlamourIdRegex = new(@"/glamour/(?<id>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SlotLinkRegex = new(@"/glamours/(?<type>[a-z]+)/\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TitleTagRegex = new(@"<title[^>]*>(?<inner>.*?)</title>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex WhiteSpaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly HttpClient client;

    public EorzeaCollectionService()
    {
        client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AllMyGlams", "0.2.0.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
    }

    public void Dispose() => client.Dispose();

    public async Task<EorzeaImportResult> ImportAsync(string input, GameDataService gameData, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        if (!TryNormalizeUrl(input, out var uri, out var externalId, out var validationMessage))
            return new EorzeaImportResult(false, null, validationMessage, warnings);

        string html;
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new EorzeaImportResult(false, null,
                    "Eorzea Collection returned 403 for this request. AllMyGlams will not try to bypass their access controls; try the full glamour URL later or open the source in your browser.", warnings);
            }

            if (!response.IsSuccessStatusCode)
                return new EorzeaImportResult(false, null, $"Eorzea Collection returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).", warnings);

            if (response.Content.Headers.ContentLength is > 4_000_000)
                return new EorzeaImportResult(false, null, "The Eorzea Collection response was unexpectedly large, so the import was cancelled.", warnings);

            html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (html.Length > 4_000_000)
                return new EorzeaImportResult(false, null, "The Eorzea Collection response was unexpectedly large, so the import was cancelled.", warnings);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new EorzeaImportResult(false, null, "The Eorzea Collection request timed out.", warnings);
        }
        catch (Exception ex)
        {
            return new EorzeaImportResult(false, null, $"Could not fetch Eorzea Collection: {ex.Message}", warnings);
        }

        try
        {
            var title = ExtractFirstClassText(html, "b-title-text-bold");
            if (string.IsNullOrWhiteSpace(title))
            {
                var titleMatch = TitleTagRegex.Match(html);
                if (titleMatch.Success)
                    title = CleanHtmlText(titleMatch.Groups["inner"].Value).Split('|', 2)[0].Trim();
            }
            if (string.IsNullOrWhiteSpace(title))
                title = $"Eorzea Collection {externalId}";

            var author = ExtractFirstClassText(html, "b-user-info-text-name");

            var outfit = OutfitRecord.CreateBlank(title);
            outfit.EnsureSlots();
            foreach (var slot in GlamSlots.Ordered)
            {
                outfit.Slots[slot].ItemId = 0;
                outfit.Slots[slot].Stain1 = 0;
                outfit.Slots[slot].Stain2 = 0;
                outfit.Slots[slot].Apply = true;
            }

            outfit.SourceName = "Eorzea Collection";
            outfit.SourceExternalId = externalId;
            outfit.SourceUrl = uri.ToString();
            outfit.SourceAuthor = string.IsNullOrWhiteSpace(author) ? null : author;
            outfit.SourceLastRefreshed = DateTimeOffset.UtcNow;
            outfit.SourceRating = null;

            var resolved = 0;
            var ringCount = 0;
            foreach (var block in ExtractDivBlocksByClass(html, "b-info-box-item-wrapper"))
            {
                var slotMatch = SlotLinkRegex.Match(block);
                if (!slotMatch.Success)
                    continue;

                var slot = MapSlot(slotMatch.Groups["type"].Value, ref ringCount);
                if (slot is null)
                    continue;

                var englishItemName = ExtractFirstClassText(block, "c-gear-slot-item-name");
                if (string.IsNullOrWhiteSpace(englishItemName))
                    continue;

                var item = gameData.ResolveEnglishItem(englishItemName, slot.Value);
                if (item is null)
                {
                    warnings.Add($"Could not resolve {slot.Value.DisplayName()}: {englishItemName}");
                    continue;
                }

                var target = outfit.Slots[slot.Value];
                target.ItemId = item.Id;
                target.Stain1 = 0;
                target.Stain2 = 0;

                var dyes = ExtractClassTexts(block, "c-gear-slot-item-info-color");
                if (dyes.Count > 0)
                    target.Stain1 = ResolveDye(dyes[0], gameData, warnings, slot.Value, 1);
                if (dyes.Count > 1)
                    target.Stain2 = ResolveDye(dyes[1], gameData, warnings, slot.Value, 2);

                resolved++;
            }

            var main = outfit.Slots[GlamSlot.MainHand];
            var off = outfit.Slots[GlamSlot.OffHand];
            if (main.ItemId != 0 && off.ItemId == 0 && gameData.ItemFitsSlot(main.ItemId, GlamSlot.OffHand))
            {
                off.ItemId = main.ItemId;
                off.Stain1 = main.Stain1;
                off.Stain2 = main.Stain2;
            }

            if (resolved == 0)
            {
                return new EorzeaImportResult(false, null,
                    "The page loaded, but no supported equipment entries could be resolved. Eorzea Collection may have changed its page structure.", warnings);
            }

            var message = $"Imported '{outfit.Name}' from Eorzea Collection ({resolved} resolved item(s)); the recipe is now local and does not need another request to wear.";
            return new EorzeaImportResult(true, outfit, message, warnings);
        }
        catch (Exception ex)
        {
            return new EorzeaImportResult(false, null, $"The Eorzea Collection page could not be parsed: {ex.Message}", warnings);
        }
    }

    private static IReadOnlyList<string> ExtractDivBlocksByClass(string html, string className)
    {
        var blocks = new List<string>();
        var searchAt = 0;

        while (searchAt < html.Length)
        {
            var classAt = html.IndexOf(className, searchAt, StringComparison.OrdinalIgnoreCase);
            if (classAt < 0)
                break;

            var open = html.LastIndexOf("<div", classAt, StringComparison.OrdinalIgnoreCase);
            if (open < 0)
            {
                searchAt = classAt + className.Length;
                continue;
            }

            var openEnd = html.IndexOf('>', open);
            if (openEnd < 0 || openEnd < classAt)
            {
                searchAt = classAt + className.Length;
                continue;
            }

            var end = FindBalancedDivEnd(html, open);
            if (end <= open)
            {
                searchAt = openEnd + 1;
                continue;
            }

            blocks.Add(html.Substring(open, end - open));
            searchAt = end;
        }

        return blocks;
    }

    private static int FindBalancedDivEnd(string html, int start)
    {
        var depth = 0;
        var pos = start;
        while (pos < html.Length)
        {
            var nextOpen = html.IndexOf("<div", pos, StringComparison.OrdinalIgnoreCase);
            var nextClose = html.IndexOf("</div", pos, StringComparison.OrdinalIgnoreCase);

            if (nextClose < 0)
                return -1;

            if (nextOpen >= 0 && nextOpen < nextClose)
            {
                depth++;
                pos = nextOpen + 4;
                continue;
            }

            depth--;
            var closeEnd = html.IndexOf('>', nextClose);
            if (closeEnd < 0)
                return -1;
            pos = closeEnd + 1;

            if (depth == 0)
                return pos;
        }

        return -1;
    }

    private static string ExtractFirstClassText(string html, string className)
        => ExtractClassTexts(html, className).FirstOrDefault() ?? string.Empty;

    private static List<string> ExtractClassTexts(string html, string className)
    {
        var escaped = Regex.Escape(className);
        var pattern = $@"<(?<tag>[a-z0-9]+)[^>]*class\s*=\s*[\"']([^\"']*\b{escaped}\b[^\"']*)[\"'][^>]*>(?<inner>.*?)</\k<tag>\s*>";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return regex.Matches(html)
            .Select(match => CleanHtmlText(match.Groups["inner"].Value))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
    }

    private static string CleanHtmlText(string value)
    {
        var withoutTags = TagRegex.Replace(value, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return WhiteSpaceRegex.Replace(decoded, " ").Trim();
    }

    private static byte ResolveDye(string text, GameDataService gameData, List<string> warnings, GlamSlot slot, int channel)
    {
        text = text.Replace("⬤", string.Empty, StringComparison.Ordinal)
            .Replace("◯", string.Empty, StringComparison.Ordinal)
            .Trim();
        text = Regex.Replace(text, @"^Dye\s*[12]\s*[:\-]?\s*", string.Empty, RegexOptions.IgnoreCase).Trim();

        if (gameData.TryResolveEnglishStain(text, out var id))
            return id;

        if (!string.IsNullOrWhiteSpace(text))
            warnings.Add($"Could not resolve {slot.DisplayName()} dye {channel}: {text}");
        return 0;
    }

    private static GlamSlot? MapSlot(string type, ref int ringCount)
        => type.ToLowerInvariant() switch
        {
            "weapon" => GlamSlot.MainHand,
            "offhand" => GlamSlot.OffHand,
            "head" => GlamSlot.Head,
            "body" => GlamSlot.Body,
            "hands" => GlamSlot.Hands,
            "legs" => GlamSlot.Legs,
            "feet" => GlamSlot.Feet,
            "earrings" => GlamSlot.Ears,
            "necklace" => GlamSlot.Neck,
            "bracelet" or "bracelets" => GlamSlot.Wrists,
            "ring" => ringCount++ == 0 ? GlamSlot.RFinger : GlamSlot.LFinger,
            _ => null,
        };

    private static bool TryNormalizeUrl(string input, out Uri uri, out string externalId, out string message)
    {
        input = input.Trim();
        externalId = string.Empty;
        message = string.Empty;

        if (long.TryParse(input, out var numericId) && numericId > 0)
            input = $"{BaseUrl}/glamour/{numericId}";

        if (!Uri.TryCreate(input, UriKind.Absolute, out uri!)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("ffxiv.eorzeacollection.com", StringComparison.OrdinalIgnoreCase))
        {
            message = "Paste an https://ffxiv.eorzeacollection.com/glamour/... URL (or a numeric glamour ID).";
            return false;
        }

        var match = GlamourIdRegex.Match(uri.AbsolutePath);
        if (!match.Success)
        {
            message = "That is an Eorzea Collection URL, but it is not an individual /glamour/{id}/... page.";
            return false;
        }

        externalId = match.Groups["id"].Value;
        return true;
    }
}
