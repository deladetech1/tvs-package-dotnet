# Changelog

## 1.0.0 (2026-05-17)

- Initial .NET 10 port of `trovesuite` (Python).
- `AuthService` — JWT decode, multi-tenant authorization, permission checks.
- `NotificationService` — Gmail SMTP via MailKit (SMS stubbed).
- `StorageService` — Azure Blob with Managed Identity, SAS via user delegation key.
- `Helper` — OTP, unique IDs, JWT encode, activity logging, tenant-aware notifications.
- `DatabaseManager` — Npgsql + Dapper, no ORM, no migrations.
- Health, Auth, Notification, and Storage Minimal API endpoint mappers.
- SQL scripts in `sql/` mirrored from the Python package; package does NOT create tables.
