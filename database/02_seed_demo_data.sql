/*
    Optional demo data: one site, one meter, five readings over two UTC days.
    Same data as the API's DemoDataSeeder. Only inserts if the Sites table is empty.

    Expected summary for meter DEMO-0001, 2026-11-01T00:00:00Z to 2026-11-02T23:59:59Z:
        total = 61.2 kWh
        2026-11-01 = 30.0 kWh  (12.5 + 17.5)
        2026-11-02 = 31.2 kWh  (13.7 + 17.5)
*/

USE [EnergyMeter];
GO

IF NOT EXISTS (SELECT 1 FROM [Sites])
BEGIN
    SET NOCOUNT ON;

    DECLARE @SiteId int, @MeterId int;

    INSERT INTO [Sites] ([Name], [CreatedUtc]) VALUES (N'Demo Site', SYSUTCDATETIME());
    SET @SiteId = SCOPE_IDENTITY();

    INSERT INTO [Meters] ([SiteId], [SerialNumber], [CreatedUtc]) VALUES (@SiteId, N'DEMO-0001', SYSUTCDATETIME());
    SET @MeterId = SCOPE_IDENTITY();

    INSERT INTO [Readings] ([MeterId], [TimestampUtc], [ValueKwh]) VALUES
        (@MeterId, '2026-11-01T00:00:00', 12500.000),
        (@MeterId, '2026-11-01T08:00:00', 12512.500),
        (@MeterId, '2026-11-01T18:00:00', 12530.000),
        (@MeterId, '2026-11-02T06:00:00', 12543.700),
        (@MeterId, '2026-11-02T18:00:00', 12561.200);

    PRINT CONCAT('Seeded site ', @SiteId, ', meter ', @MeterId, '.');
END
ELSE
BEGIN
    PRINT 'Sites table is not empty; demo data skipped.';
END;
GO
