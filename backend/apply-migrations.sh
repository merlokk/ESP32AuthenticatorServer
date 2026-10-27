#!/bin/sh
#
# Applies every module schema's migrations, in order, and stops at the first failure: a failed
# migration is a failed release, and the backend does not roll. Each bundle reads its own schema's
# history table, so running the Job again is a no-op.
#
# The connection string is the backend's own variable, so the Job takes the backend's secret
# as is (envFrom). A role with DDL rights; today the app and the migrations share one role.

set -eu

if [ -z "${ConnectionStrings__AuthServer:-}" ]; then
    echo "ConnectionStrings__AuthServer is not set: Host=...;Port=5432;Database=authserver;Username=...;Password=..." >&2
    exit 2
fi

# A bundle builds the host's service provider; nothing in it needs more than the connection.
SCHEMAS="identity sync"

for schema in ${SCHEMAS}; do
    echo "--- ${schema} ---"
    "/migrations/${schema}/efbundle" --connection "${ConnectionStrings__AuthServer}"
done

echo "All schemas are up to date."
