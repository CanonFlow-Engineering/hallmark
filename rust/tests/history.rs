use hallmark_core::{History, LedgerEntry, Migration, inspect_history, migration_checksum};

fn migration(version: u32) -> Migration {
    Migration {
        version,
        script: format!("V{version}__wallet.sql"),
        checksum: -123,
    }
}

fn row(migration: &Migration) -> LedgerEntry {
    LedgerEntry {
        version: Some(migration.version.to_string()),
        script: migration.script.clone(),
        kind: "SQL".to_owned(),
        checksum: Some(migration.checksum),
        success: true,
    }
}

#[test]
fn flyway_checksum_preserves_utf8_ignoring_bom_and_line_terminators() {
    let checksum = migration_checksum("123456789");
    assert_eq!(checksum, -873_187_034);
    assert_eq!(migration_checksum("\u{feff}123\r\n456\r789\n"), checksum);
    assert_ne!(migration_checksum("123456780"), checksum);
    assert_eq!(
        migration_checksum("Wällét\n"),
        migration_checksum("Wällét\r")
    );
}

#[test]
fn compares_the_whole_ordered_history() {
    let first = migration(1);
    let second = migration(2);
    let third = migration(3);
    let history = [row(&first), row(&second)];
    let prefix = [first, second];
    assert_eq!(
        inspect_history(&prefix, &third, &history),
        History::ExpectedPredecessor
    );
    assert_eq!(
        inspect_history(&prefix, &third, &history[1..]),
        History::Diverged
    );
    let mut full = Vec::from(history);
    full.push(row(&third));
    assert_eq!(
        inspect_history(&prefix, &third, &full),
        History::CurrentMigration {
            checksum: third.checksum
        }
    );
    full.push(row(&third));
    assert_eq!(inspect_history(&prefix, &third, &full), History::Diverged);
}

#[test]
fn rejects_failed_renamed_and_edited_ledger_entries() {
    let first = migration(1);
    let second = migration(2);
    let prefix = [first];
    let mut history = [row(&prefix[0]), row(&second)];
    history[1].success = false;
    assert_eq!(
        inspect_history(&prefix, &second, &history),
        History::Diverged
    );
    history[1].success = true;
    history[1].script = "V2__different.sql".to_owned();
    assert_eq!(
        inspect_history(&prefix, &second, &history),
        History::Diverged
    );
    history[1] = row(&second);
    history[1].checksum = Some(20);
    assert_eq!(
        inspect_history(&prefix, &second, &history),
        History::CurrentMigration { checksum: 20 }
    );
    history[0].checksum = Some(20);
    assert_eq!(
        inspect_history(&prefix, &second, &history),
        History::Diverged
    );
}
