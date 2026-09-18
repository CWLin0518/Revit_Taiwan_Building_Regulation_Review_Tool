# P1-T03 ReviewPackage Persistence

## Contract

- `ReviewPackage` implements the fields in spec section 6 and validates its required identity fields.
- Current logical record version is `1.0`.
- `IReviewPackageRepository` exposes `Get`, `GetAll`, `Save` (create/update), and `Delete`.
- The Revit adapter stores one package in one `DataStorage` element using Extensible Storage schema GUID `d3272af5-4945-4be6-bf9f-a5a6202bd306`.
- Every property is a separate Extensible Storage field. The payload is not an opaque JSON blob, and Package ID can be queried from the entity.
- Revit `Save` and `Delete` require the caller to own an open transaction. Reads do not open a transaction.

## Versioning

- The logical `SchemaVersion` field is independent of the immutable Revit schema name/GUID.
- `ReviewPackageStorageMigrator` upgrades legacy `0.9` records to `1.0`, normalizing missing list fields.
- Unknown versions fail explicitly instead of being silently interpreted.
- Future physical field changes must use a new Revit schema GUID and add an adapter-level migration; do not mutate the released schema definition behind the existing GUID.

## Verification

- Solution build succeeds against the Revit 2024 API with zero warnings and zero errors.
- Core tests cover validation, normalization, complete storage-record round trip, `0.9` migration, and rejection of unknown versions.
- Live create/update/delete/reopen verification requires wiring this repository into a Revit external command. That entry point belongs to a later integration task and is not added here.
