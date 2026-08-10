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

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] nvarchar(450) NOT NULL,
        [FullName] nvarchar(150) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NULL,
        [UserName] nvarchar(150) NOT NULL,
        [Action] nvarchar(50) NOT NULL,
        [EntityName] nvarchar(100) NOT NULL,
        [EntityId] nvarchar(50) NULL,
        [Details] nvarchar(max) NULL,
        [IpAddress] nvarchar(64) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [Brands] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Brands] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [Coupons] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [DiscountPercentage] decimal(5,2) NOT NULL,
        [IsActive] bit NOT NULL,
        [StartDate] datetime2 NULL,
        [EndDate] datetime2 NULL,
        [MaxUsageCount] int NULL,
        [UsedCount] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Coupons] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [Categories] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [BrandId] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Categories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Categories_Brands_BrandId] FOREIGN KEY ([BrandId]) REFERENCES [Brands] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [Invoices] (
        [Id] int NOT NULL IDENTITY,
        [InvoiceNumber] nvarchar(30) NOT NULL,
        [AgentId] nvarchar(450) NOT NULL,
        [CustomerName] nvarchar(150) NULL,
        [CustomerPhone] nvarchar(30) NULL,
        [SubTotal] decimal(18,2) NOT NULL,
        [CouponId] int NULL,
        [CouponCode] nvarchar(50) NULL,
        [DiscountPercentage] decimal(5,2) NOT NULL,
        [DiscountAmount] decimal(18,2) NOT NULL,
        [Total] decimal(18,2) NOT NULL,
        [Status] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Invoices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Invoices_AspNetUsers_AgentId] FOREIGN KEY ([AgentId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Invoices_Coupons_CouponId] FOREIGN KEY ([CouponId]) REFERENCES [Coupons] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [Products] (
        [Id] int NOT NULL IDENTITY,
        [Barcode] nvarchar(64) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [StockQuantity] int NOT NULL,
        [LowStockThreshold] int NOT NULL,
        [BrandId] int NOT NULL,
        [CategoryId] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Products_Brands_BrandId] FOREIGN KEY ([BrandId]) REFERENCES [Brands] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Products_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [InvoiceItems] (
        [Id] int NOT NULL IDENTITY,
        [InvoiceId] int NOT NULL,
        [ProductId] int NOT NULL,
        [ProductName] nvarchar(200) NOT NULL,
        [Barcode] nvarchar(64) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [Quantity] int NOT NULL,
        [LineTotal] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_InvoiceItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InvoiceItems_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [Invoices] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_InvoiceItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE TABLE [StockMovements] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [Change] int NOT NULL,
        [QuantityAfter] int NOT NULL,
        [Reason] int NOT NULL,
        [ReferenceInvoiceId] int NULL,
        [UserId] nvarchar(450) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StockMovements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockMovements_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_CreatedAt] ON [AuditLogs] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_UserId] ON [AuditLogs] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Brands_Name] ON [Brands] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Categories_BrandId_Name] ON [Categories] ([BrandId], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Coupons_Code] ON [Coupons] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InvoiceItems_InvoiceId] ON [InvoiceItems] ([InvoiceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InvoiceItems_ProductId] ON [InvoiceItems] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Invoices_AgentId_CreatedAt] ON [Invoices] ([AgentId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Invoices_CouponId] ON [Invoices] ([CouponId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Invoices_CreatedAt] ON [Invoices] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Invoices_InvoiceNumber] ON [Invoices] ([InvoiceNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Products_Barcode] ON [Products] ([Barcode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_BrandId_CategoryId] ON [Products] ([BrandId], [CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_CategoryId] ON [Products] ([CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockMovements_CreatedAt] ON [StockMovements] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_StockMovements_ProductId] ON [StockMovements] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803123607_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260803123607_InitialCreate', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    ALTER TABLE [StockMovements] ADD [ReferenceReturnId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    ALTER TABLE [Invoices] ADD [CustomerId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    ALTER TABLE [Invoices] ADD [RefundedAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    ALTER TABLE [InvoiceItems] ADD [ReturnedQuantity] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE TABLE [Customers] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [Phone] nvarchar(30) NOT NULL,
        [PhoneNormalized] nvarchar(30) NOT NULL,
        [Notes] nvarchar(500) NULL,
        [Address] nvarchar(200) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Customers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE TABLE [Returns] (
        [Id] int NOT NULL IDENTITY,
        [ReturnNumber] nvarchar(30) NOT NULL,
        [InvoiceId] int NOT NULL,
        [InvoiceNumber] nvarchar(30) NOT NULL,
        [ProcessedByUserId] nvarchar(450) NOT NULL,
        [ProcessedByName] nvarchar(150) NOT NULL,
        [CustomerId] int NULL,
        [CustomerName] nvarchar(150) NULL,
        [CustomerPhone] nvarchar(30) NULL,
        [Reason] int NOT NULL,
        [Notes] nvarchar(500) NULL,
        [SubTotal] decimal(18,2) NOT NULL,
        [DiscountPercentage] decimal(5,2) NOT NULL,
        [DiscountAmount] decimal(18,2) NOT NULL,
        [RefundAmount] decimal(18,2) NOT NULL,
        [RestockedToInventory] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Returns] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Returns_AspNetUsers_ProcessedByUserId] FOREIGN KEY ([ProcessedByUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Returns_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Returns_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [Invoices] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE TABLE [ReturnItems] (
        [Id] int NOT NULL IDENTITY,
        [ReturnId] int NOT NULL,
        [InvoiceItemId] int NOT NULL,
        [ProductId] int NOT NULL,
        [ProductName] nvarchar(200) NOT NULL,
        [Barcode] nvarchar(64) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [Quantity] int NOT NULL,
        [LineTotal] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_ReturnItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReturnItems_InvoiceItems_InvoiceItemId] FOREIGN KEY ([InvoiceItemId]) REFERENCES [InvoiceItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ReturnItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ReturnItems_Returns_ReturnId] FOREIGN KEY ([ReturnId]) REFERENCES [Returns] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE INDEX [IX_Invoices_CustomerId_CreatedAt] ON [Invoices] ([CustomerId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE INDEX [IX_Invoices_CustomerPhone] ON [Invoices] ([CustomerPhone]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE INDEX [IX_Customers_Name] ON [Customers] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Customers_PhoneNormalized] ON [Customers] ([PhoneNormalized]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE INDEX [IX_ReturnItems_InvoiceItemId] ON [ReturnItems] ([InvoiceItemId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE INDEX [IX_ReturnItems_ProductId] ON [ReturnItems] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE INDEX [IX_ReturnItems_ReturnId] ON [ReturnItems] ([ReturnId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE INDEX [IX_Returns_CreatedAt] ON [Returns] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE INDEX [IX_Returns_CustomerId] ON [Returns] ([CustomerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE INDEX [IX_Returns_InvoiceId] ON [Returns] ([InvoiceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE INDEX [IX_Returns_ProcessedByUserId_CreatedAt] ON [Returns] ([ProcessedByUserId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Returns_ReturnNumber] ON [Returns] ([ReturnNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    ALTER TABLE [Invoices] ADD CONSTRAINT [FK_Invoices_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE SET NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804074557_AddCustomersAndReturns'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804074557_AddCustomersAndReturns', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    ALTER TABLE [StockMovements] ADD [Note] nvarchar(300) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    ALTER TABLE [StockMovements] ADD [ReferenceAdjustmentId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    ALTER TABLE [StockMovements] ADD [ReferencePurchaseReceiptId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    ALTER TABLE [StockMovements] ADD [ReferenceTransferId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    ALTER TABLE [StockMovements] ADD [WarehouseId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    ALTER TABLE [Returns] ADD [WarehouseId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    ALTER TABLE [Invoices] ADD [WarehouseId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [WarehouseId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    CREATE TABLE [Warehouses] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(20) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Address] nvarchar(300) NULL,
        [Phone] nvarchar(30) NULL,
        [ManagerName] nvarchar(150) NULL,
        [IsActive] bit NOT NULL,
        [IsDefault] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Warehouses] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    CREATE TABLE [ProductStocks] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [WarehouseId] int NOT NULL,
        [Quantity] int NOT NULL,
        [LowStockThreshold] int NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_ProductStocks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductStocks_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductStocks_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    CREATE INDEX [IX_AspNetUsers_WarehouseId] ON [AspNetUsers] ([WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductStocks_ProductId_WarehouseId] ON [ProductStocks] ([ProductId], [WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    CREATE INDEX [IX_ProductStocks_WarehouseId_Quantity] ON [ProductStocks] ([WarehouseId], [Quantity]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Warehouses_Code] ON [Warehouses] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    CREATE INDEX [IX_Warehouses_IsDefault] ON [Warehouses] ([IsDefault]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    CREATE INDEX [IX_Warehouses_Name] ON [Warehouses] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD CONSTRAINT [FK_AspNetUsers_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE SET NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN

    IF NOT EXISTS (SELECT 1 FROM [Warehouses] WHERE [Code] = N'MAIN')
    BEGIN
        INSERT INTO [Warehouses] ([Code], [Name], [Address], [Phone], [ManagerName], [IsActive], [IsDefault], [CreatedAt])
        VALUES (N'MAIN', N'المخزن الرئيسي', NULL, NULL, NULL, 1, 1, GETDATE());
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN

    INSERT INTO [ProductStocks] ([ProductId], [WarehouseId], [Quantity], [LowStockThreshold], [UpdatedAt])
    SELECT p.[Id], w.[Id], p.[StockQuantity], NULL, GETDATE()
    FROM [Products] p
    CROSS JOIN (SELECT TOP 1 [Id] FROM [Warehouses] WHERE [Code] = N'MAIN') w
    WHERE NOT EXISTS (
        SELECT 1 FROM [ProductStocks] ps
        WHERE ps.[ProductId] = p.[Id] AND ps.[WarehouseId] = w.[Id]
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN

    UPDATE [StockMovements]
    SET [WarehouseId] = (SELECT TOP 1 [Id] FROM [Warehouses] WHERE [Code] = N'MAIN')
    WHERE [WarehouseId] IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN

    UPDATE [Invoices]
    SET [WarehouseId] = (SELECT TOP 1 [Id] FROM [Warehouses] WHERE [Code] = N'MAIN')
    WHERE [WarehouseId] IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN

    UPDATE r
    SET r.[WarehouseId] = i.[WarehouseId]
    FROM [Returns] r
    JOIN [Invoices] i ON i.[Id] = r.[InvoiceId]
    WHERE r.[WarehouseId] IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN

    UPDATE u
    SET u.[WarehouseId] = (SELECT TOP 1 [Id] FROM [Warehouses] WHERE [Code] = N'MAIN')
    FROM [AspNetUsers] u
    WHERE u.[WarehouseId] IS NULL
      AND EXISTS (
          SELECT 1
          FROM [AspNetUserRoles] ur
          JOIN [AspNetRoles] r ON r.[Id] = ur.[RoleId]
          WHERE ur.[UserId] = u.[Id] AND r.[Name] <> N'Admin'
      );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StockMovements]') AND [c].[name] = N'WarehouseId');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [StockMovements] DROP CONSTRAINT [' + @var0 + '];');
    EXEC(N'UPDATE [StockMovements] SET [WarehouseId] = 0 WHERE [WarehouseId] IS NULL');
    ALTER TABLE [StockMovements] ALTER COLUMN [WarehouseId] int NOT NULL;
    ALTER TABLE [StockMovements] ADD DEFAULT 0 FOR [WarehouseId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    CREATE INDEX [IX_StockMovements_WarehouseId_ProductId_CreatedAt] ON [StockMovements] ([WarehouseId], [ProductId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    ALTER TABLE [StockMovements] ADD CONSTRAINT [FK_StockMovements_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Invoices]') AND [c].[name] = N'WarehouseId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Invoices] DROP CONSTRAINT [' + @var1 + '];');
    EXEC(N'UPDATE [Invoices] SET [WarehouseId] = 0 WHERE [WarehouseId] IS NULL');
    ALTER TABLE [Invoices] ALTER COLUMN [WarehouseId] int NOT NULL;
    ALTER TABLE [Invoices] ADD DEFAULT 0 FOR [WarehouseId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    CREATE INDEX [IX_Invoices_WarehouseId_CreatedAt] ON [Invoices] ([WarehouseId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    ALTER TABLE [Invoices] ADD CONSTRAINT [FK_Invoices_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Returns]') AND [c].[name] = N'WarehouseId');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Returns] DROP CONSTRAINT [' + @var2 + '];');
    EXEC(N'UPDATE [Returns] SET [WarehouseId] = 0 WHERE [WarehouseId] IS NULL');
    ALTER TABLE [Returns] ALTER COLUMN [WarehouseId] int NOT NULL;
    ALTER TABLE [Returns] ADD DEFAULT 0 FOR [WarehouseId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    CREATE INDEX [IX_Returns_WarehouseId_CreatedAt] ON [Returns] ([WarehouseId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    ALTER TABLE [Returns] ADD CONSTRAINT [FK_Returns_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804100032_AddWarehousesAndProductStock'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804100032_AddWarehousesAndProductStock', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804104958_AddStockTransfers'
)
BEGIN
    CREATE TABLE [StockTransfers] (
        [Id] int NOT NULL IDENTITY,
        [TransferNumber] nvarchar(30) NOT NULL,
        [FromWarehouseId] int NOT NULL,
        [ToWarehouseId] int NOT NULL,
        [Status] int NOT NULL,
        [Notes] nvarchar(500) NULL,
        [RejectionReason] nvarchar(300) NULL,
        [RequestedByUserId] nvarchar(450) NOT NULL,
        [RequestedByName] nvarchar(150) NOT NULL,
        [RequestedAt] datetime2 NOT NULL,
        [ApprovedByUserId] nvarchar(450) NULL,
        [ApprovedByName] nvarchar(150) NULL,
        [ApprovedAt] datetime2 NULL,
        [ShippedByUserId] nvarchar(450) NULL,
        [ShippedByName] nvarchar(150) NULL,
        [ShippedAt] datetime2 NULL,
        [ReceivedByUserId] nvarchar(450) NULL,
        [ReceivedByName] nvarchar(150) NULL,
        [ReceivedAt] datetime2 NULL,
        CONSTRAINT [PK_StockTransfers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockTransfers_Warehouses_FromWarehouseId] FOREIGN KEY ([FromWarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockTransfers_Warehouses_ToWarehouseId] FOREIGN KEY ([ToWarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804104958_AddStockTransfers'
)
BEGIN
    CREATE TABLE [StockTransferItems] (
        [Id] int NOT NULL IDENTITY,
        [StockTransferId] int NOT NULL,
        [ProductId] int NOT NULL,
        [ProductName] nvarchar(200) NOT NULL,
        [Barcode] nvarchar(64) NOT NULL,
        [RequestedQuantity] int NOT NULL,
        [ShippedQuantity] int NOT NULL,
        [ReceivedQuantity] int NOT NULL,
        CONSTRAINT [PK_StockTransferItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StockTransferItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StockTransferItems_StockTransfers_StockTransferId] FOREIGN KEY ([StockTransferId]) REFERENCES [StockTransfers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804104958_AddStockTransfers'
)
BEGIN
    CREATE INDEX [IX_StockTransferItems_ProductId] ON [StockTransferItems] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804104958_AddStockTransfers'
)
BEGIN
    CREATE INDEX [IX_StockTransferItems_StockTransferId] ON [StockTransferItems] ([StockTransferId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804104958_AddStockTransfers'
)
BEGIN
    CREATE INDEX [IX_StockTransfers_FromWarehouseId_Status] ON [StockTransfers] ([FromWarehouseId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804104958_AddStockTransfers'
)
BEGIN
    CREATE INDEX [IX_StockTransfers_RequestedAt] ON [StockTransfers] ([RequestedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804104958_AddStockTransfers'
)
BEGIN
    CREATE INDEX [IX_StockTransfers_ToWarehouseId_Status] ON [StockTransfers] ([ToWarehouseId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804104958_AddStockTransfers'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockTransfers_TransferNumber] ON [StockTransfers] ([TransferNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804104958_AddStockTransfers'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804104958_AddStockTransfers', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE TABLE [Suppliers] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [Phone] nvarchar(30) NOT NULL,
        [PhoneNormalized] nvarchar(30) NOT NULL,
        [Email] nvarchar(150) NULL,
        [Address] nvarchar(200) NULL,
        [ContactPerson] nvarchar(150) NULL,
        [Notes] nvarchar(500) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Suppliers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE TABLE [PurchaseOrders] (
        [Id] int NOT NULL IDENTITY,
        [PurchaseNumber] nvarchar(30) NOT NULL,
        [SupplierId] int NOT NULL,
        [SupplierName] nvarchar(150) NOT NULL,
        [WarehouseId] int NOT NULL,
        [Status] int NOT NULL,
        [SubTotal] decimal(18,2) NOT NULL,
        [DiscountPercentage] decimal(5,2) NOT NULL,
        [DiscountAmount] decimal(18,2) NOT NULL,
        [Total] decimal(18,2) NOT NULL,
        [ExpectedDate] datetime2 NULL,
        [Notes] nvarchar(500) NULL,
        [CancellationReason] nvarchar(300) NULL,
        [CreatedByUserId] nvarchar(450) NOT NULL,
        [CreatedByName] nvarchar(150) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ConfirmedByUserId] nvarchar(450) NULL,
        [ConfirmedByName] nvarchar(150) NULL,
        [ConfirmedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        CONSTRAINT [PK_PurchaseOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseOrders_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseOrders_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE TABLE [PurchaseOrderItems] (
        [Id] int NOT NULL IDENTITY,
        [PurchaseOrderId] int NOT NULL,
        [ProductId] int NOT NULL,
        [ProductName] nvarchar(200) NOT NULL,
        [Barcode] nvarchar(64) NOT NULL,
        [UnitCost] decimal(18,2) NOT NULL,
        [Quantity] int NOT NULL,
        [ReceivedQuantity] int NOT NULL,
        [LineTotal] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_PurchaseOrderItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseOrderItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseOrderItems_PurchaseOrders_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [PurchaseOrders] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE TABLE [PurchaseReceipts] (
        [Id] int NOT NULL IDENTITY,
        [ReceiptNumber] nvarchar(30) NOT NULL,
        [PurchaseOrderId] int NOT NULL,
        [WarehouseId] int NOT NULL,
        [ReceivedByUserId] nvarchar(450) NOT NULL,
        [ReceivedByName] nvarchar(150) NOT NULL,
        [Notes] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_PurchaseReceipts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseReceipts_PurchaseOrders_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [PurchaseOrders] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_PurchaseReceipts_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE TABLE [PurchaseReceiptItems] (
        [Id] int NOT NULL IDENTITY,
        [PurchaseReceiptId] int NOT NULL,
        [PurchaseOrderItemId] int NOT NULL,
        [ProductId] int NOT NULL,
        [ProductName] nvarchar(200) NOT NULL,
        [Quantity] int NOT NULL,
        CONSTRAINT [PK_PurchaseReceiptItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseReceiptItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseReceiptItems_PurchaseOrderItems_PurchaseOrderItemId] FOREIGN KEY ([PurchaseOrderItemId]) REFERENCES [PurchaseOrderItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseReceiptItems_PurchaseReceipts_PurchaseReceiptId] FOREIGN KEY ([PurchaseReceiptId]) REFERENCES [PurchaseReceipts] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderItems_ProductId] ON [PurchaseOrderItems] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderItems_PurchaseOrderId] ON [PurchaseOrderItems] ([PurchaseOrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_CreatedAt] ON [PurchaseOrders] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PurchaseOrders_PurchaseNumber] ON [PurchaseOrders] ([PurchaseNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_SupplierId_CreatedAt] ON [PurchaseOrders] ([SupplierId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_WarehouseId_Status] ON [PurchaseOrders] ([WarehouseId], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE INDEX [IX_PurchaseReceiptItems_ProductId] ON [PurchaseReceiptItems] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE INDEX [IX_PurchaseReceiptItems_PurchaseOrderItemId] ON [PurchaseReceiptItems] ([PurchaseOrderItemId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE INDEX [IX_PurchaseReceiptItems_PurchaseReceiptId] ON [PurchaseReceiptItems] ([PurchaseReceiptId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE INDEX [IX_PurchaseReceipts_CreatedAt] ON [PurchaseReceipts] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE INDEX [IX_PurchaseReceipts_PurchaseOrderId] ON [PurchaseReceipts] ([PurchaseOrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PurchaseReceipts_ReceiptNumber] ON [PurchaseReceipts] ([ReceiptNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE INDEX [IX_PurchaseReceipts_WarehouseId] ON [PurchaseReceipts] ([WarehouseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE INDEX [IX_Suppliers_Name] ON [Suppliers] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Suppliers_PhoneNormalized] ON [Suppliers] ([PhoneNormalized]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804112852_AddPurchasing'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804112852_AddPurchasing', N'8.0.11');
END;
GO

COMMIT;
GO

