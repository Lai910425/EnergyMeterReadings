/*
    Energy Meter Readings - schema script (SQL Server 2016+)

    Creates the same schema as the EF Core migration 20261116092000_InitialCreate and records
    that migration in __EFMigrationsHistory, so the API (and `dotnet ef database update`) treat
    the database as up to date.

    Safe to run more than once: every step is skipped if the migration is already recorded.

    Usage (SSMS): open this file, run it. Or from a terminal:
        sqlcmd -S localhost,14333 -U sa -P "EnergyMeter_Dev_123!" -C -i database\01_create_schema.sql
    Change the database name below if you want something other than EnergyMeter
    (and update ConnectionStrings:EnergyMeterDb to match).
*/

IF DB_ID(N'EnergyMeter') IS NULL
BEGIN
    CREATE DATABASE [EnergyMeter];
END;
GO

USE [EnergyMeter];
GO

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261116092000_InitialCreate')
BEGIN
    CREATE TABLE [Sites] (
        [Id] int NOT NULL IDENTITY(1, 1),
        [Name] nvarchar(200) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Sites] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261116092000_InitialCreate')
BEGIN
    CREATE TABLE [Meters] (
        [Id] int NOT NULL IDENTITY(1, 1),
        [SiteId] int NOT NULL,
        [SerialNumber] nvarchar(100) NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Meters] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Meters_Sites_SiteId] FOREIGN KEY ([SiteId]) REFERENCES [Sites] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261116092000_InitialCreate')
BEGIN
    CREATE TABLE [Readings] (
        [Id] bigint NOT NULL IDENTITY(1, 1),
        [MeterId] int NOT NULL,
        [TimestampUtc] datetime2 NOT NULL,
        [ValueKwh] decimal(18,3) NOT NULL,
        CONSTRAINT [PK_Readings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Readings_Meters_MeterId] FOREIGN KEY ([MeterId]) REFERENCES [Meters] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261116092000_InitialCreate')
BEGIN
    CREATE INDEX [IX_Meters_SiteId] ON [Meters] ([SiteId]);
END;
GO

-- Uniqueness rule: for a given meter, each reading timestamp must be unique.
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261116092000_InitialCreate')
BEGIN
    CREATE UNIQUE INDEX [IX_Readings_MeterId_TimestampUtc] ON [Readings] ([MeterId], [TimestampUtc]);
END;
GO

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261116092000_InitialCreate')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261116092000_InitialCreate', N'8.0.11');
END;
GO

COMMIT;
GO
