using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace SummaryGenerator.Services
{
    public partial class PdfTextExtractor(ILogger<PdfTextExtractor> logger) : IPdfTextExtractor
    {
        public Task<string> ExtractTextAsync(string pdfPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
            {
                throw new ArgumentException("PDF path is required.", nameof(pdfPath));
            }

            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException("PDF file was not found.", pdfPath);
            }

            cancellationToken.ThrowIfCancellationRequested();

            List<List<string>> pages;

            try
            {
                using var document = PdfDocument.Open(pdfPath);
                pages = document.GetPages()
                    .Select(page =>
                        page.Text
                            .Split(Environment.NewLine, StringSplitOptions.TrimEntries)
                            .Where(line => !string.IsNullOrWhiteSpace(line))
                            .ToList())
                    .Where(lines => lines.Count > 0)
                    .ToList();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse PDF file {PdfPath}.", pdfPath);
                throw new InvalidDataException("The PDF is unreadable or corrupt.", ex);
            }

            if (pages.Count == 0)
            {
                return Task.FromResult(string.Empty);
            }

            var repeatedHeaders = GetRepeatedBorderLines(pages, true);
            var repeatedFooters = GetRepeatedBorderLines(pages, false);
            var cleanedPages = pages.Select(lines => CleanPageLines(lines, repeatedHeaders, repeatedFooters)).ToList();

            var output = new StringBuilder();
            for (var i = 0; i < cleanedPages.Count; i++)
            {
                if (i > 0)
                {
                    output.AppendLine();
                    output.AppendLine();
                }

                foreach (var line in cleanedPages[i])
                {
                    output.AppendLine(line);
                }
            }

            var cleanedText = output.ToString().Trim();
            logger.LogInformation("Extracted text from {PdfPath}. Characters: {CharacterCount}", pdfPath, cleanedText.Length);
            return Task.FromResult(cleanedText);
        }

        private static HashSet<string> GetRepeatedBorderLines(IReadOnlyCollection<List<string>> pages, bool first)
        {
            var candidates = pages
                .Select(lines => first ? lines.FirstOrDefault() : lines.LastOrDefault())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line!.Trim())
                .GroupBy(line => line, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return candidates;
        }

        private static List<string> CleanPageLines(
            List<string> lines,
            HashSet<string> repeatedHeaders,
            HashSet<string> repeatedFooters)
        {
            var working = new List<string>(lines);

            while (working.Count > 0 && repeatedHeaders.Contains(working[0].Trim()))
            {
                working.RemoveAt(0);
            }

            while (working.Count > 0 && repeatedFooters.Contains(working[^1].Trim()))
            {
                working.RemoveAt(working.Count - 1);
            }

            return working
                .Where(line => !PageMarkerRegex().IsMatch(line.Trim()))
                .ToList();
        }

        [GeneratedRegex(@"^(page\s+)?\d+(\s+of\s+\d+)?$", RegexOptions.IgnoreCase)]
        private static partial Regex PageMarkerRegex();
    }
}
