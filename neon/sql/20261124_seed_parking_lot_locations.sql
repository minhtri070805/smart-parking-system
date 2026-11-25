BEGIN;

UPDATE parking_lots
SET name = 'Bãi xe FPT University HCMC',
    address = 'Lô E2a-7, Đường D1, Khu Công nghệ cao, Phường Tăng Nhơn Phú, TP. Hồ Chí Minh',
    latitude = 10.8411276,
    longitude = 106.809883,
    updated_at = now()
WHERE id = 8 AND name = 'FTown 1';

UPDATE parking_lots
SET name = 'Bãi xe Landmark 81',
    address = '720A Điện Biên Phủ, Phường Thạnh Mỹ Tây, TP. Hồ Chí Minh',
    latitude = 10.795037,
    longitude = 106.721859,
    updated_at = now()
WHERE id = 7 AND name = 'Landmark 103';

UPDATE parking_lots
SET name = 'Bãi xe Saigon Centre',
    address = '65 Lê Lợi, Phường Sài Gòn, TP. Hồ Chí Minh',
    latitude = 10.77306,
    longitude = 106.70083,
    updated_at = now()
WHERE id = 5 AND name = 'Landmark 92';

UPDATE parking_lots
SET name = 'Bãi xe Vincom Center Đồng Khởi',
    address = '72 Lê Thánh Tôn, Phường Sài Gòn, TP. Hồ Chí Minh',
    latitude = 10.778103,
    longitude = 106.70195,
    updated_at = now()
WHERE id = 2 AND name = 'Lot 1';

UPDATE parking_lots
SET name = 'Bãi xe Sân bay Tân Sơn Nhất',
    address = 'Đường Trường Sơn, TP. Hồ Chí Minh',
    latitude = 10.81889,
    longitude = 106.65194,
    updated_at = now()
WHERE id = 4 AND name = 'Lot 2';

COMMIT;
