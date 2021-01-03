create database [__DBNAME__]
go

use [__DBNAME__]
go

-- Files we have observed as input from WSUS. These are wims, exe's, cabs, who knows.
CREATE TABLE dbo.[wsusFile]
(
	ID int NOT NULL PRIMARY KEY IDENTITY(1,1),
	filename nvarchar(1000) NOT NULL
)

-- Every file we have extracted from any update has a row in this table.
CREATE TABLE dbo.[files]
(
	ID int NOT NULL PRIMARY KEY IDENTITY(1,1),
	wsusFileID int NOT NULL,
	filename nvarchar(1000) NOT NULL,
	hash_sha256 binary(32) NOT NULL,
	size BIGINT NOT NULL,

	rsds_GUID nvarchar(33) NULL,
	rsds_age int NULL,
	rsds_filename nvarchar(200) NULL,
	authenticode_certfriendly nvarchar(200) NULL,
	authenticode_certsubj nvarchar(500) NULL,

	FileDescription nvarchar(500) NULL,
    FileVersion nvarchar(100) NULL,
    ProductName nvarchar(500) NULL,
    ProductVersion nvarchar(100) NULL,
    Comments nvarchar(MAX) NULL,
    CompanyName nvarchar(500) NULL
)
ALTER TABLE dbo.[files] ADD FOREIGN KEY (wsusFileID) REFERENCES dbo.[wsusFile](ID);
CREATE UNIQUE NONCLUSTERED INDEX fileHashIndex ON [dbo].[files] ( [hash_sha256] ASC)


-- An entry in a given WIM image, eg, "windows 10 professional".
CREATE TABLE dbo.[fileSource_wim]
(
	ID int NOT NULL PRIMARY KEY IDENTITY(1,1),
	wimFileID int not NULL,
	wimImageIndex int NOT NULL,
	wimImageSize BIGINT NOT NULL,
	wimImageName nvarchar(200) NOT NULL,
	wimImageDescription nvarchar(200) NOT NULL
)
ALTER TABLE dbo.[fileSource_wim] ADD FOREIGN KEY (wimFileID) REFERENCES dbo.[wsusFile](ID);

-- Extra information about files found from WIM images.
CREATE TABLE dbo.[files_wim]
(
	ID int NOT NULL PRIMARY KEY IDENTITY(1,1),
	sourceID_wim int NOT NULL,
	fileID int NOT NULL
)
ALTER TABLE dbo.[files_wim] ADD FOREIGN KEY (sourceID_wim) REFERENCES dbo.[fileSource_wim](ID);
ALTER TABLE dbo.[files_wim] ADD FOREIGN KEY (fileID) REFERENCES dbo.[files](ID);
CREATE UNIQUE NONCLUSTERED INDEX sourceAndFileIDs ON [dbo].[files_wim] ( sourceID_wim ASC, fileID ASC)

CREATE TABLE dbo.[errors]
(
	ID int NOT NULL PRIMARY KEY IDENTITY(1,1),
	fileID int NOT NULL,
	exceptionType nvarchar(200) not null,
	exceptionString nvarchar(MAX) not null
)
ALTER TABLE dbo.[errors] ADD FOREIGN KEY (fileID) REFERENCES dbo.[wsusFile](ID);


