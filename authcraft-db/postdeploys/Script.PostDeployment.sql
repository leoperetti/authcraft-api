/*
Plantilla de script posterior a la implementación							
--------------------------------------------------------------------------------------
 Este archivo contiene instrucciones de SQL que se anexarán al script de compilación.		
 Use la sintaxis de SQLCMD para incluir un archivo en el script posterior a la implementación.			
 Ejemplo:      :r .\miArchivo.sql								
 Use la sintaxis de SQLCMD para hacer referencia a una variable en el script posterior a la implementación.		
 Ejemplo:      :setvar TableName miTabla							
               SELECT * FROM [$(TableName)]					
--------------------------------------------------------------------------------------
*/
IF @@TRANCOUNT <> 0
    THROW 51001, 'Postdeployment requires a separate transaction.', 1;

SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;

-- Inserción inicial de proveedores si no existen
IF NOT EXISTS (SELECT 1 FROM dbo.providers WHERE name = 'google')
	INSERT INTO dbo.providers (name) VALUES ('google');
IF NOT EXISTS (SELECT 1 FROM dbo.providers WHERE name = 'apple')
	INSERT INTO dbo.providers (name) VALUES ('apple');
IF NOT EXISTS (SELECT 1 FROM dbo.providers WHERE name = 'github')
	INSERT INTO dbo.providers (name) VALUES ('github');

:r .\ValidateConstraints.sql

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
