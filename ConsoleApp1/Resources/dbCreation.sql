create database [__DBNAME__]
go

use [__DBNAME__]
go

-- Files we have observed as input from WSUS. These are wims, exe's, cabs, who knows.
CREATE TABLE dbo.[wsusFile]
(
	ID int NOT NULL PRIMARY KEY IDENTITY(1,1),
	filename nvarchar(1000) NOT NULL,
	downloadURI nvarchar(3000) NOT NULL,
	fileHashFromWSUS binary(20) NOT NULL,
	sizeBytes BIGINT NOT NULL,
	status nvarchar(20) NOT NULL default 'QUEUED'
)
GO
CREATE NONCLUSTERED INDEX [wsusFileSizeIndex] ON [dbo].[wsusFile] ( sizeBytes );
CREATE INDEX [IX_wsusFile_fileHashFromWSUS] ON [dbo].[wsusFile] ([fileHashFromWSUS])
-- Since we do smaller (or larger) files frst, we need an index on status which covers the file size to keep things not-slow.
CREATE INDEX [wsusFileSizeStatusIndex] ON [dbo].[wsusFile] ([status]) INCLUDE ([sizeBytes])

-- perf stats, per-wsus-file
CREATE TABLE dbo.[wsusFileStats]
(
	ID int NOT NULL PRIMARY KEY IDENTITY(1,1),
	hostname nvarchar(30) null,
	wsusFileID int NOT NULL,
	startTime datetime,
	endTime datetime,
	sqltime datetime
)
ALTER TABLE dbo.[wsusFileStats] ADD FOREIGN KEY (wsusFileID) REFERENCES dbo.[wsusFile](ID);

-- Every file we have extracted from any update has a row in this table.
CREATE TABLE dbo.[files]
(
	ID int NOT NULL PRIMARY KEY IDENTITY(1,1),
	inserttime datetime  default getdate() not null,
	wsusFileID int NULL,

	filelocation nvarchar(1000) NULL,
	filename nvarchar(1000) NULL,
	fileextension nvarchar(100) NULL,

	hash_sha256 binary(32) NOT NULL,
	size BIGINT NULL,

	pe_timestamp binary(6) null,
	pe_sizeOfCode binary(6) null,
	pe_magicType binary(2) null,
	contents128b binary(128) null,

	rsds_GUID nvarchar(33) NULL,
	rsds_age int NULL,
	rsds_filename nvarchar(200) NULL,
	authenticode_certfriendly nvarchar(200) NULL,
	authenticode_certsubj nvarchar(500) NULL,

	FileDescription nvarchar(500) NULL,
    FileVersion nvarchar(500) NULL,
    ProductName nvarchar(500) NULL,
    ProductVersion nvarchar(100) NULL,
    Comments nvarchar(MAX) NULL,
    CompanyName nvarchar(500) NULL
)
ALTER TABLE dbo.[files] ADD FOREIGN KEY (wsusFileID) REFERENCES dbo.[wsusFile](ID);
CREATE UNIQUE NONCLUSTERED INDEX fileHashIndex ON [dbo].[files] ( [hash_sha256] );
CREATE NONCLUSTERED INDEX fileextensionIndex ON [dbo].[files] ( [fileextension] );
CREATE NONCLUSTERED INDEX filenameIndex ON [dbo].[files] ( [filename] );
CREATE INDEX [IX_files_wsusFileID] ON [dbo].[files] ([wsusFileID])

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

-- Extra information about CAB files.
CREATE TABLE dbo.[filesource_cab]
(
	ID int NOT NULL PRIMARY KEY IDENTITY(1,1),
	wsusFileID int NOT NULL,
	offlineCapable tinyint NULL,
	description nvarchar(100) NULL,
	productName nvarchar(100) NULL,
	supportInfo nvarchar(100) NULL
)
ALTER TABLE dbo.[filesource_cab] ADD FOREIGN KEY (wsusFileID) REFERENCES dbo.wsusFile(ID);
CREATE UNIQUE NONCLUSTERED INDEX wsusFileID ON [dbo].[filesource_cab] ( wsusFileID ASC)

CREATE TABLE dbo.[errors]
(
	ID int NOT NULL PRIMARY KEY IDENTITY(1,1),
	srcurl nvarchar(1000) null,
	exceptionType nvarchar(200) not null,
	exceptionString nvarchar(MAX) not null
)


create table deltas(
	ID int NOT NULL PRIMARY KEY IDENTITY(1,1),
    sourceFileID  int null,
    sourceFileSize  int not null,
    outputFileID  int null,
    outputFileSize  int not null,
    deltaFileID int not null,
    deltaFileSize int not null	
)

-- Call this SP with a table var containing 'file' rows. They will be safely batch inserted.
CREATE type FileTableType as TABLE 
(
	filelocation nvarchar(1000) NOT NULL,
	filename nvarchar(1000) NOT NULL,
	fileextension nvarchar(100) NOT NULL,

	hash_sha256 binary(32) NOT NULL,
	size BIGINT NOT NULL,

	pe_timestamp binary(6) null,
	pe_sizeOfCode binary(6) null,
	pe_magicType binary(2) null,
	contents128b binary(128) not null,

	rsds_GUID nvarchar(33) NULL,
	rsds_age int NULL,
	rsds_filename nvarchar(200) NULL,
	authenticode_certfriendly nvarchar(200) NULL,
	authenticode_certsubj nvarchar(500) NULL,

	FileDescription nvarchar(500) NULL,
    FileVersion nvarchar(500) NULL,
    ProductName nvarchar(500) NULL,
    ProductVersion nvarchar(100) NULL,
    Comments nvarchar(MAX) NULL,
    CompanyName nvarchar(500) NULL
)

go
CREATE PROCEDURE insertFiles
(   
	@parentWsusFileHash as binary(20),
   @TblVar as FileTableType READONLY  
)   
AS  

	DECLARE @insertedID int

	-- for selecting from the table var
	DECLARE @wsusFileID int
	DECLARE @filelocation nvarchar(1000)
	DECLARE @filename nvarchar(1000)
	DECLARE @fileextension nvarchar(100)
	DECLARE @hash_sha256 binary(32)
	DECLARE @size BIGINT
	DECLARE @pe_timestamp binary(6)
	DECLARE @pe_sizeOfCode binary(6)
	DECLARE @pe_magicType binary(2)
	DECLARE @contents128b binary(128)
	DECLARE @rsds_GUID nvarchar(33)
	DECLARE @rsds_age int
	DECLARE @rsds_filename nvarchar(200)
	DECLARE @authenticode_certfriendly nvarchar(200)
	DECLARE @authenticode_certsubj nvarchar(500)
	DECLARE @FileDescription nvarchar(500)
	DECLARE @FileVersion nvarchar(500)
	DECLARE @ProductName nvarchar(500)
	DECLARE @ProductVersion nvarchar(100)
	DECLARE @Comments nvarchar(MAX)
	DECLARE @CompanyName nvarchar(500)

	-- gotta get the ID of the parent WSUS update. It is assumed that all files come from the same update.
	select @wsusFileID = id from wsusfile where filehashfromwsus = @parentWsusFileHash

	DECLARE inputCursor CURSOR FOR SELECT 
		filelocation, filename, fileextension, 
		hash_sha256, size, pe_timestamp, pe_sizeOfCode, pe_magicType, 
		contents128b, rsds_GUID, rsds_age, rsds_filename, 
		authenticode_certfriendly, authenticode_certsubj, 
		FileDescription, FileVersion, ProductName, ProductVersion, Comments, CompanyName
	FROM @TblVar

	OPEN inputCursor 
	FETCH NEXT FROM inputCursor INTO 
		@filelocation, @filename, @fileextension, 
		@hash_sha256, @size, @pe_timestamp, @pe_sizeOfCode, @pe_magicType, 
		@contents128b, @rsds_GUID, @rsds_age, @rsds_filename, 
		@authenticode_certfriendly, @authenticode_certsubj, 
		@FileDescription, @FileVersion, @ProductName, @ProductVersion, @Comments, @CompanyName

	WHILE @@FETCH_STATUS = 0
	BEGIN
		-- early out without taking SERIALIZABLE if we can
		if not exists( select * from files where hash_sha256 = @hash_sha256)
		begin
			set @insertedID = 1
			BEGIN TRAN
				-- Just merge the hash, we will add the rest of the fields once we release the merge locks.
				MERGE files WITH (SERIALIZABLE) AS T 
				USING(VALUES (@hash_sha256)) 
				AS U(hash_sha256)
				ON (
					T.hash_sha256 = U.hash_sha256 
				) 
				WHEN MATCHED THEN 
					-- if the row is already present, another query is dealing with it. No need for us to do anything.
					UPDATE SET @insertedID = -1
				WHEN NOT MATCHED THEN 
					-- Otherwise, insert a new row containing our hash.
					INSERT(hash_sha256) VALUES(hash_sha256);
				if @insertedID = 1
				begin
					set @insertedID = SCOPE_IDENTITY()
				end
			commit

			if @insertedID != -1
			begin
				-- now that we've added the row, we can just do a normal insert without requiring SERIALIZABLE.
				update files set 
					wsusFileID=@wsusFileID, filelocation=@filelocation, filename=@filename, fileextension=@fileextension, 
					size=@size, pe_timestamp=@pe_timestamp, pe_sizeOfCode=@pe_sizeOfCode, pe_magicType=@pe_magicType, contents128b=@contents128b, 
					rsds_GUID=@rsds_GUID, rsds_age=@rsds_age, rsds_filename=@rsds_filename, 
					authenticode_certfriendly=@authenticode_certfriendly, authenticode_certsubj=@authenticode_certsubj, 
					FileDescription=@FileDescription, FileVersion=@FileVersion, ProductName=@ProductName, ProductVersion=@ProductVersion, Comments=@Comments, CompanyName=@CompanyName
					where files.ID = @insertedID
			end
		end
			
		FETCH NEXT FROM inputCursor INTO 
			@filelocation, @filename, @fileextension, 
			@hash_sha256, @size, @pe_timestamp, @pe_sizeOfCode, @pe_magicType, 
			@contents128b, @rsds_GUID, @rsds_age, @rsds_filename, 
			@authenticode_certfriendly, @authenticode_certsubj, 
			@FileDescription, @FileVersion, @ProductName, @ProductVersion, @Comments, @CompanyName
	end

	close inputCursor
	deallocate inputCursor
go


-- Likewise, call this SP with a table var containing 'file_wim' rows. They will be safely batch inserted, with extra info about the WIM being inserted if it
-- is not already present.
-- We assume that no other threads are accessing the file_wim table while we run, but that they may be accessing the files table.
CREATE PROCEDURE insertFiles_wim
(   
	-- The WSUS file hash to associate this with
	@parentWsusFileHash as binary(20),

	-- Information about the WIM which this files comes from, assumed to be the same for all rows
	@wimImageIndex int,
	@wimImageSize BIGINT,
	@wimImageName nvarchar(200),
	@wimImageDescription nvarchar(200),

	-- Finally, a large amount of files to associate.
	@TblVar as FileTableType READONLY  
)   
AS  

	-- for selecting from the table var
	DECLARE @wsusFileID int
	DECLARE @filelocation nvarchar(1000)
	DECLARE @filename nvarchar(1000)
	DECLARE @fileextension nvarchar(100)
	DECLARE @hash_sha256 binary(32)
	DECLARE @size BIGINT
	DECLARE @pe_timestamp binary(6)
	DECLARE @pe_sizeOfCode binary(6)
	DECLARE @pe_magicType binary(2)
	DECLARE @contents128b binary(128)
	DECLARE @rsds_GUID nvarchar(33)
	DECLARE @rsds_age int
	DECLARE @rsds_filename nvarchar(200)
	DECLARE @authenticode_certfriendly nvarchar(200)
	DECLARE @authenticode_certsubj nvarchar(500)
	DECLARE @FileDescription nvarchar(500)
	DECLARE @FileVersion nvarchar(500)
	DECLARE @ProductName nvarchar(500)
	DECLARE @ProductVersion nvarchar(100)
	DECLARE @Comments nvarchar(MAX)
	DECLARE @CompanyName nvarchar(500)

	-- gotta get the ID of the parent WSUS update. It is assumed that all files come from the same update.
	select @wsusFileID = id from wsusfile where filehashfromwsus = @parentWsusFileHash

	-- Insert the filesource_wim, if it isn't already present. Assume there is no-one else accessing the table and that it is safe to do this non-atomically.
	-- again, assume all files are coming from this same filesource_wim.
	DECLARE @wimFileID int;
	select @wimFileID=id from filesource_wim where wimImageName = @wimImageName and wimImageIndex = @wimImageIndex;
	if @wimFileID is null
	begin
		insert into filesource_wim 
			(wimFileID, wimImageIndex, wimImageSize, wimImageName, wimImageDescription) 
		values 
			(@wsusFileID, @wimImageIndex, @wimImageSize, @wimImageName, @wimImageDescription) 
		set @wimFileID = SCOPE_IDENTITY()
	end

	-- Now we've inserted our parent source, it's time to insert all the files.
	DECLARE inputCursor CURSOR FOR SELECT 
		filelocation, filename, fileextension, 
		hash_sha256, size, pe_timestamp, pe_sizeOfCode, pe_magicType, 
		contents128b, rsds_GUID, rsds_age, rsds_filename, 
		authenticode_certfriendly, authenticode_certsubj, 
		FileDescription, FileVersion, ProductName, ProductVersion, Comments, CompanyName
	FROM @TblVar

	OPEN inputCursor 
	FETCH NEXT FROM inputCursor INTO 
		@filelocation, @filename, @fileextension, 
		@hash_sha256, @size, @pe_timestamp, @pe_sizeOfCode, @pe_magicType, 
		@contents128b, @rsds_GUID, @rsds_age, @rsds_filename, 
		@authenticode_certfriendly, @authenticode_certsubj, 
		@FileDescription, @FileVersion, @ProductName, @ProductVersion, @Comments, @CompanyName

	WHILE @@FETCH_STATUS = 0
	BEGIN
		-- Only insert the file if we need to (rows are added w/ heavy concurrency but never removed)
		DECLARE @insertedID int
		select @insertedID = null
		select @insertedID = id from files where hash_sha256 = @hash_sha256
		if @insertedID is null
		begin
			DECLARE @didInsert int

			BEGIN TRAN
				-- Just merge the hash, we will add the rest of the fields once we release the merge locks.
				MERGE files WITH (SERIALIZABLE) AS T 
				USING(VALUES (@insertedID, @hash_sha256)) 
				AS U(id, hash_sha256)
				ON (
					T.hash_sha256 = U.hash_sha256 
				) 
				WHEN MATCHED THEN 
					-- if the row is already present, another query is dealing with it. No need for us to do anything.
					UPDATE SET @insertedID = T.id, @didInsert = 0
				WHEN NOT MATCHED THEN 
					-- Otherwise, insert a new row containing our hash.
					INSERT(hash_sha256) VALUES(hash_sha256);
					set @insertedID = SCOPE_IDENTITY()
					set @didInsert = 1
			commit

			if @didInsert = 1
			begin
				-- now that we've added the row, we can just do a normal insert without requiring SERIALIZABLE.
				update files set 
					wsusFileID=@wsusFileID, filelocation=@filelocation, filename=@filename, fileextension=@fileextension, 
					size=@size, pe_timestamp=@pe_timestamp, pe_sizeOfCode=@pe_sizeOfCode, pe_magicType=@pe_magicType, contents128b=@contents128b, 
					rsds_GUID=@rsds_GUID, rsds_age=@rsds_age, rsds_filename=@rsds_filename, 
					authenticode_certfriendly=@authenticode_certfriendly, authenticode_certsubj=@authenticode_certsubj, 
					FileDescription=@FileDescription, FileVersion=@FileVersion, ProductName=@ProductName, ProductVersion=@ProductVersion, Comments=@Comments, CompanyName=@CompanyName
					where files.ID = @insertedID
			end
		end

		-- We must then add the file_wim row, linking a WIM to a file. We don't need to worry about other threads here, since we are the only thread dealing
		-- with this WIM, but we do check that a previous run of the application hasn't already filled this row.

		if not exists (select id from files_wim where sourceID_wim = @wimFileID and fileID = @insertedID)
		begin
			insert into files_wim (sourceID_wim, fileID) values (@wimFileID, @insertedID) 
		end
			
		FETCH NEXT FROM inputCursor INTO 
			@filelocation, @filename, @fileextension, 
			@hash_sha256, @size, @pe_timestamp, @pe_sizeOfCode, @pe_magicType, 
			@contents128b, @rsds_GUID, @rsds_age, @rsds_filename, 
			@authenticode_certfriendly, @authenticode_certsubj, 
			@FileDescription, @FileVersion, @ProductName, @ProductVersion, @Comments, @CompanyName
	end

	close inputCursor
	deallocate inputCursor
go
