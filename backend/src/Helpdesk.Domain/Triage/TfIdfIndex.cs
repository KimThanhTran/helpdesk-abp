using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Helpdesk.Triage;

/// <summary>
/// A small text-similarity index (TF-IDF with cosine similarity). It answers one question:
/// "which of these documents is the most like this text?". It runs in memory, with no model to train or download.
/// </summary>
public sealed partial class TfIdfIndex
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "but", "if", "of", "to", "in", "on", "at", "by", "for", "with", "from", "as",
        "is", "are", "was", "were", "be", "been", "am", "it", "its", "this", "that", "these", "those", "there",
        "i", "me", "my", "we", "our", "you", "your", "they", "them", "their", "he", "she", "his", "her",
        "do", "does", "did", "have", "has", "had", "not", "no", "can", "could", "will", "would", "should", "please",
        "hi", "hello", "thanks", "thank", "just", "very", "so", "also", "when", "what", "how", "why", "which", "who",
        "any", "some", "all", "about", "after", "before", "again", "now", "still", "get", "got", "need", "want",
    };

    private readonly Dictionary<string, double> _idf = new(StringComparer.Ordinal);
    private readonly List<(Guid Id, Dictionary<string, double> Vector)> _documents = new();

    public TfIdfIndex(IEnumerable<(Guid Id, string Text)> documents)
    {
        var tokenised = documents.Select(document => (document.Id, Terms: Tokenize(document.Text))).ToList();

        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, terms) in tokenised)
        {
            foreach (var term in terms.Distinct())
            {
                documentFrequency[term] = documentFrequency.GetValueOrDefault(term) + 1;
            }
        }

        // smoothed inverse document frequency: a word in every document still counts a little, a rare one counts a lot
        var count = tokenised.Count;
        foreach (var (term, frequency) in documentFrequency)
        {
            _idf[term] = Math.Log((count + 1.0) / (frequency + 1.0)) + 1.0;
        }

        foreach (var (id, terms) in tokenised)
        {
            _documents.Add((id, Vectorize(terms)));
        }
    }

    public int Count => _documents.Count;

    /// <summary>The <paramref name="take"/> documents most like <paramref name="query"/>, best first, with scores from 0 to 1.</summary>
    public IReadOnlyList<(Guid Id, double Score)> Search(
        string query, int take, Guid? exclude = null, double minScore = 0.0)
    {
        var queryVector = Vectorize(Tokenize(query));
        if (queryVector.Count == 0)
        {
            return Array.Empty<(Guid, double)>();
        }

        return _documents
            .Where(document => document.Id != exclude)
            .Select(document => (document.Id, Score: Dot(queryVector, document.Vector)))
            .Where(match => match.Score > 0 && match.Score >= minScore)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Id)
            .Take(take)
            .ToList();
    }

    /// <summary>Lower-cases, splits into words, drops filler words and trims simple endings (jams -> jam).</summary>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return WordPattern().Matches(text.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(word => word.Length >= 2 && !StopWords.Contains(word))
            .Select(Stem)
            .ToList();
    }

    private Dictionary<string, double> Vectorize(IReadOnlyList<string> terms)
    {
        var vector = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var group in terms.GroupBy(term => term))
        {
            if (_idf.TryGetValue(group.Key, out var idf))
            {
                // sublinear term frequency: saying a word ten times is not ten times as important
                vector[group.Key] = (1.0 + Math.Log(group.Count())) * idf;
            }
        }

        var norm = Math.Sqrt(vector.Values.Sum(weight => weight * weight));
        if (norm > 0)
        {
            foreach (var term in vector.Keys.ToList())
            {
                vector[term] /= norm;
            }
        }

        return vector;
    }

    private static double Dot(Dictionary<string, double> small, Dictionary<string, double> large)
    {
        if (small.Count > large.Count)
        {
            (small, large) = (large, small);
        }

        var sum = 0.0;
        foreach (var (term, weight) in small)
        {
            if (large.TryGetValue(term, out var other))
            {
                sum += weight * other;
            }
        }

        return sum;
    }

    private static string Stem(string word)
    {
        // shortcut: three English endings only, no real stemmer (dropping stays "dropp", not "drop");
        // add a proper stemmer if tickets arrive in another language or recall matters more
        if (word.Length > 5 && word.EndsWith("ing", StringComparison.Ordinal))
        {
            return word[..^3];
        }

        if (word.Length > 4 && word.EndsWith("ed", StringComparison.Ordinal))
        {
            return word[..^2];
        }

        if (word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal))
        {
            return word[..^1];
        }

        return word;
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}
