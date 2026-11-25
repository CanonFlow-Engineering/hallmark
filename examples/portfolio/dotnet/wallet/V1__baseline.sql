CREATE TABLE wallets(id text PRIMARY KEY,balance numeric(18,2) NOT NULL CHECK(balance>=0));
CREATE TABLE operations(reference text PRIMARY KEY,wallet text NOT NULL,amount numeric(18,2) NOT NULL CHECK(amount>0),status text NOT NULL,balance numeric(18,2) NOT NULL);
CREATE TABLE outbox(reference text PRIMARY KEY REFERENCES operations(reference),event text NOT NULL);

INSERT INTO wallets VALUES ('a', 85), ('b', 100);
INSERT INTO operations VALUES ('legacy', 'a', 15, 'accepted', 85);
INSERT INTO outbox VALUES ('legacy', 'debited');
