# Setup guide

1. Install .NET 10 SDK.
2. Build: `dotnet build AomrModEditor.sln`
3. Import **your** extracted gameplay directory (never commit retail XML).
4. Use CLI or UI against the loopback API with the ephemeral bearer token.

# Safe workflow

- Prefer additive `proto_mods.xml` / `techtree_mods.xml`.
- Validate before generate; resolve fingerprint mismatches via migration (never silent reuse).
- Install copies only generated mod files.

# Data structure

See `aomr_extract/` research notes and `fixtures/synthetic/` for synthetic shapes.
