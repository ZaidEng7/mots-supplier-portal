# Backups

Two scripts and one test. `backup.sh` writes a timestamped run directory holding a custom-format
database dump, its checksum, and a mirror of the document bucket. `restore.sh` puts one of those
back. `BackupRestoreDrillTests` proves on every push that a dump of this schema restores with its
rows and its triggers intact, so the restore path is exercised rather than described.

Each script explains its own decisions in its header. What follows is what an operator needs.

## Taking a backup

```bash
export DATABASE_URL='postgresql://user:password@host:5432/mots_supplier_portal'
export BACKUP_DESTINATION=/var/backups/mots
export MINIO_ALIAS=production      # an alias already configured with `mc alias set`
export BACKUP_KEEP=14              # run directories to keep; 0 disables pruning
ops/backup/backup.sh
```

Without `MINIO_ALIAS` the database is backed up and uploaded documents are not. The script says so
on stderr rather than passing silently, because a backup missing every supplier's registration
documents still restores cleanly and looks fine.

## Restoring

```bash
export DATABASE_URL='postgresql://user:password@host:5432/mots_supplier_portal'
RESTORE_CONFIRM=yes ops/backup/restore.sh /var/backups/mots/2026-09-15T02-00-00Z
```

It refuses without `RESTORE_CONFIRM=yes`, verifies the checksum before touching the database, and
stops at the first error instead of leaving a half-restored database that exits 0.

Restore into a scratch database first whenever the target is production and the emergency allows it.

## Scheduling

Anything that can run a shell command on a schedule: cron, a systemd timer, the platform's own job
runner. Hourly is a reasonable starting point and sets the recovery point at one hour.

```
0 * * * * DATABASE_URL=... BACKUP_DESTINATION=/var/backups/mots /opt/mots/ops/backup/backup.sh >> /var/log/mots-backup.log 2>&1
```

The job's own failure has to be visible. A backup script that has been exiting non-zero for three
weeks into a log nobody reads is the failure mode this whole directory exists to prevent, and it
looks exactly like a working backup until the day it is needed.

## What this does not cover

Each of these is infrastructure, needs decisions this repository cannot make, and is deliberately
not pretended to be in place:

- **Point-in-time recovery.** The requirements set a 15-minute recovery point. A periodic dump gives
  a recovery point of one interval, so meeting that number needs continuous WAL archiving to durable
  storage and a base backup to replay onto. Until that exists, the honest recovery point is however
  often the job above is scheduled.
- **Off-site storage.** The scripts write where they are pointed. A backup on the database host
  survives only the failures that were never going to lose the data.
- **Encryption at rest.** The dump contains personal data and encrypted field values, and nothing
  here encrypts the file. The destination has to provide that.
- **A restore rehearsal against real production data.** The drill proves the mechanism on this
  schema. It does not prove that a particular production backup, on particular hardware, restores
  inside whatever time the ministry can tolerate being down. That needs a rehearsal on a real copy,
  and its result is the only thing that turns a recovery-time target into a measured number.
