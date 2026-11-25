BEGIN TRANSACTION;
CREATE TABLE sales_v2 (
    sale_id VARCHAR PRIMARY KEY,
    region VARCHAR NOT NULL,
    amount DECIMAL(12, 2) NOT NULL CHECK (amount >= 0),
    amount_cents BIGINT NOT NULL CHECK (amount_cents >= 0)
);
INSERT INTO sales_v2
    SELECT sale_id, region, amount, CAST(amount * 100 AS BIGINT)
    FROM sales;
DROP TABLE sales;
ALTER TABLE sales_v2 RENAME TO sales;
CREATE VIEW region_totals AS
    SELECT region, count(*)::INTEGER AS sales, sum(amount_cents)::BIGINT AS cents
    FROM sales
    GROUP BY region;
COMMIT;

COPY (SELECT * FROM region_totals ORDER BY region)
TO 'out/region_totals.parquet' (FORMAT PARQUET, COMPRESSION ZSTD);
