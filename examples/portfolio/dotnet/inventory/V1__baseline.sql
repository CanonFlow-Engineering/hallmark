CREATE SCHEMA inventory;
CREATE TABLE inventory.stock (
    sku text PRIMARY KEY,
    available integer NOT NULL CHECK (available >= 0),
    version bigint NOT NULL
);
CREATE TABLE inventory.reservations (
    request_id text PRIMARY KEY,
    sku text NOT NULL,
    quantity integer NOT NULL,
    remaining integer NOT NULL,
    version bigint NOT NULL
);
CREATE TABLE inventory.outbox (id text PRIMARY KEY, kind text NOT NULL);

INSERT INTO inventory.stock VALUES ('widget', 96, 1);
INSERT INTO inventory.reservations VALUES ('legacy', 'widget', 4, 96, 1);
INSERT INTO inventory.outbox VALUES ('legacy', 'reserved');
