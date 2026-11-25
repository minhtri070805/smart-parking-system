START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124115720_AddParkingLotCoordinates') THEN
    ALTER TABLE parking_lots ADD latitude double precision;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124115720_AddParkingLotCoordinates') THEN
    ALTER TABLE parking_lots ADD longitude double precision;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124115720_AddParkingLotCoordinates') THEN
    ALTER TABLE parking_lots ADD CONSTRAINT chk_parking_lots_coordinates CHECK ((latitude IS NULL AND longitude IS NULL) OR (latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261124115720_AddParkingLotCoordinates') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261124115720_AddParkingLotCoordinates', '10.0.4');
    END IF;
END $EF$;
COMMIT;
