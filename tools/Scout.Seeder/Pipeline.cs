using System.Security.Cryptography;
using System.Text;
using Scout.Core;

namespace Scout.Seeder;

public sealed record SourceEntry(string Url, string SavedPath, DateTimeOffset RetrievedAt, DateTimeOffset? PublishedAt, string GameVersion, string License, string TermsCheckedAt, bool AllowIngestion, bool AllowRawRetention);
public sealed record SourceManifest(SourceEntry[] Sources);
public sealed record Candidate(string Id, ClaimKind Kind, string Subject, string? Other, string? Tag, double Weight, string GameVersion, string SourceUrl, string EvidenceType, double Confidence, string EvidenceLocator, string Reason);
public sealed record Quarantine(string Id, string Reason);
public sealed record CandidateBatch(Claim[] Claims, Quarantine[] Quarantine, string[] Conflicts);
public sealed record Review(string ClaimId, string ClaimHash, string Decision, string Reviewer, DateTimeOffset ReviewedAt);
public interface IAiExtractor { Candidate[] Extract(string responsePath); }
/// <summary>Imports saved model responses; no model, credentials or network in runtime.</summary>
public sealed class SavedResponseExtractor : IAiExtractor
{
    public Candidate[] Extract(string responsePath) => Json.Read<Candidate[]>(responsePath);
}
public static class Pipeline
{
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string ClaimHash(Claim claim) => Hash(Encoding.UTF8.GetBytes(Json.Write(claim)));
    public static SourceEntry ValidateSource(SourceManifest manifest, string url, string baseDirectory)
    {
        var entries = manifest.Sources.Where(s => s.Url == url).ToArray();
        if (entries.Length != 1) throw new InvalidDataException("Source must occur exactly once in explicit allowlist");
        var source = entries[0];
        if (!source.AllowIngestion || !source.AllowRawRetention || string.IsNullOrWhiteSpace(source.License) || !DateTimeOffset.TryParse(source.TermsCheckedAt, out var checkedAt) || checkedAt > DateTimeOffset.UtcNow || source.RetrievedAt == default || source.RetrievedAt > DateTimeOffset.UtcNow.AddMinutes(5) || source.PublishedAt > source.RetrievedAt) throw new InvalidDataException("Missing terms/retention authorization or invalid source dates");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "file")) throw new InvalidDataException("Invalid source URL");
        var path = Path.GetFullPath(source.SavedPath, baseDirectory);
        if (!File.Exists(path) || new FileInfo(path).Length > 5_000_000) throw new InvalidDataException("Missing or oversized saved source");
        return source;
    }
    public static CandidateBatch Ingest(StrategyPack catalog, SourceManifest manifest, Candidate[] candidates, string baseDirectory, string evidenceDirectory)
    {
        PackValidation.Validate(catalog);
        Directory.CreateDirectory(evidenceDirectory);
        var claims = new List<Claim>(); var quarantine = new List<Quarantine>();
        var duplicateIds = candidates.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var duplicateBodies = candidates.GroupBy(c => (c.Kind, c.Subject, c.Other, c.Tag, c.Weight, c.GameVersion, c.SourceUrl)).Where(g => g.Count() > 1).SelectMany(g => g.Select(c => c.Id)).ToHashSet();
        foreach (var c in candidates)
        {
            try
            {
                if (duplicateIds.Contains(c.Id) || duplicateBodies.Contains(c.Id) || catalog.Claims.Any(x => x.Id == c.Id)) throw new InvalidDataException("Duplicate candidate; all duplicates quarantined");
                var source = ValidateSource(manifest, c.SourceUrl, baseDirectory);
                var bytes = File.ReadAllBytes(Path.GetFullPath(source.SavedPath, baseDirectory)); var hash = Hash(bytes);
                var provenance = new Provenance(source.Url, source.RetrievedAt, source.PublishedAt, source.GameVersion, c.EvidenceType, c.Confidence, hash, source.License, c.EvidenceLocator);
                var claim = new Claim(c.Id, c.Kind, c.Subject, c.Other, c.Tag, c.Weight, c.GameVersion, provenance, "candidate", c.Reason);
                var error = PackValidation.ClaimError(claim, catalog); if (error != null) throw new InvalidDataException(error);
                // Original bytes retained only after explicit rights/retention checks, content-addressed.
                File.WriteAllBytes(Path.Combine(evidenceDirectory, hash + ".evidence"), bytes);
                claims.Add(claim);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException) { quarantine.Add(new(c.Id, ex.Message)); }
        }
        var conflicts = claims.GroupBy(c => (c.Kind, c.Subject, c.Other, c.Tag, c.GameVersion)).Where(g => g.Select(c => Math.Sign(c.Weight)).Distinct().Count() > 1).Select(g => string.Join(",", g.Select(c => c.Id).Order(StringComparer.Ordinal))).ToArray();
        return new(claims.ToArray(), quarantine.ToArray(), conflicts);
    }
    public static StrategyPack Compile(StrategyPack catalog, CandidateBatch batch, Review[] reviews, string version, string evidenceDirectory)
    {
        if (version == catalog.PackVersion || string.IsNullOrWhiteSpace(version)) throw new InvalidDataException("Use a new immutable pack version");
        if (reviews.GroupBy(r => r.ClaimId).Any(g => g.Count() != 1)) throw new InvalidDataException("Duplicate review");
        if (reviews.Any(r => !batch.Claims.Any(c => c.Id == r.ClaimId))) throw new InvalidDataException("Review references unknown claim");
        var approved = new List<Claim>();
        foreach (var claim in batch.Claims)
        {
            var r = reviews.SingleOrDefault(r => r.ClaimId == claim.Id);
            if (r == null || r.ClaimHash != ClaimHash(claim) || string.IsNullOrWhiteSpace(r.Reviewer) || r.ReviewedAt == default || r.ReviewedAt > DateTimeOffset.UtcNow || r.Decision is not ("approved" or "rejected")) throw new InvalidDataException("Every candidate requires a dated, named review of its exact content");
            var error = PackValidation.ClaimError(claim, catalog); if (error != null) throw new InvalidDataException(error);
            var evidence = Path.Combine(evidenceDirectory, claim.Source.EvidenceHash + ".evidence");
            if (!File.Exists(evidence) || Hash(File.ReadAllBytes(evidence)) != claim.Source.EvidenceHash) throw new InvalidDataException("Raw evidence missing or tampered");
            if (r.Decision == "approved") approved.Add(claim with { ReviewState = "approved" });
        }
        var pack = catalog with { PackVersion = version, Claims = catalog.Claims.Concat(approved).ToArray() };
        PackValidation.Validate(pack); return pack;
    }
}
