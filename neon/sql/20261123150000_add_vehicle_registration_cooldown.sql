START TRANSACTION;
CREATE TABLE vehicle_registration_blocks (
    id bigint GENERATED ALWAYS AS IDENTITY,
    plate_number character varying(20) NOT NULL,
    blocked_until_utc timestamp with time zone NOT NULL,
    created_at_utc timestamp with time zone NOT NULL DEFAULT (now()),
    CONSTRAINT pk_vehicle_registration_blocks PRIMARY KEY (id),
    CONSTRAINT chk_vehicle_registration_blocks_plate_normalized CHECK (plate_number = UPPER(TRIM(plate_number)))
);

CREATE UNIQUE INDEX uq_vehicle_registration_blocks_plate_number ON vehicle_registration_blocks (plate_number);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261123150000_AddVehicleRegistrationCooldown', '10.0.4');

COMMIT;
