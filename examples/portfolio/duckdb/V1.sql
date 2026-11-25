BEGIN TRANSACTION;
CREATE TABLE sales (
    sale_id VARCHAR PRIMARY KEY,
    region VARCHAR NOT NULL,
    amount DECIMAL(12, 2) NOT NULL CHECK (amount >= 0)
);
INSERT INTO sales VALUES
    ('0001', 'east', 12.50),
    ('0002', 'east', 20.00),
    ('0003', 'west', 40.00);
COMMIT;
