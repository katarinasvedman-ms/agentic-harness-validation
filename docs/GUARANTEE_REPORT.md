# Governed plan verification guarantee report

This report describes the bounded deterministic plan-verification claim. It does not claim whole-agent or model verification.

## Artifact identity

| Artifact | Version or SHA-256 |
| --- | --- |
| Executable verifier source set | `2830609c829e0b8c6b40b564e8f996df1bf6974fc4af4d6dfe5cb8cb9aee5969` |
| Dafny proof set | `65125d5e0e38300820bb90474fa2a09ab74ac713b5ae5165ce4d8eed4200bd86` |
| Verification specification | `bffce34a2f80785ae734c3ebd01302bfab04f3aed1a1111f063a6d7a95340382` |
| Plan schema | `1.0` |
| Verifier | `0.1.0` |
| LemmaScript | `0.5.19` |
| Dafny | `4.11.0` |
| Z3 | `4.12.1` |
| TypeScript | `6.0.2` |

The source and proof set digests are SHA-256 hashes of the newline-joined, path-sorted individual file hashes.

## Verified model obligations

PO-01 through PO-10 are properties of the bounded Dafny model. Their correspondence to the executable TypeScript validator is supported by parser, mutation, boundary, and differential tests; only the classification predicate is generated directly from TypeScript by LemmaScript.

| Obligation | Bounded claim | Status |
| --- | --- | --- |
| PO-01 | Accepted steps satisfy capability, registry, dependency, and production-effect guards | Verified |
| PO-02 | Ordered dependencies are acyclic | Verified |
| PO-03 | Approval validity implies exact binding and freshness | Verified |
| PO-04 | Modeled transitions preserve denial, idempotency, and kill-switch safety | Verified |
| PO-05 | Accepted modeled flows respect the classification lattice | Verified |
| PO-06 | Delegated capabilities cannot exceed parent or delegated sets | Verified |
| PO-07 | Accepted production writes declare bounded compensation | Verified |
| PO-08 | Expired plans cannot enter verified, approved, or executing states | Verified |
| PO-09 | Accepted parser inputs use known schema fields and enum values | Verified |
| PO-10 | Verification results bind to the plan digest and allowed versions | Verified |

The LemmaScript-generated classification predicate and the Dafny model contain 17 verified proof targets with zero verification errors.

## Executable conformance

- The parser rejects unknown fields, unknown enum values, malformed values, and unsupported schemas.
- The validator rejects undeclared capabilities, registry mismatches, invalid dependencies, production deletes, unsafe information flows, missing or out-of-scope compensation, and expired plans.
- The bounded differential corpus checks all 25 source/destination classification-rank pairs against the executable predicate.
- Verification-result tests reject changed plan digests and unapproved specification versions.

## Assumptions and exclusions

The normative assumptions are A-01 through A-14 in [VERIFICATION_SPEC.md](VERIFICATION_SPEC.md). In particular, trusted registry inputs, canonicalization, runtime invocation of the gate, approval integrity, and gateway-only execution remain runtime obligations.

The explicit exclusions in section 11 of [VERIFICATION_SPEC.md](VERIFICATION_SPEC.md) apply. The proof does not establish LLM correctness, natural-language safety, arbitrary information-flow security, cloud-platform correctness, external remediation success, or freedom from distributed races.
