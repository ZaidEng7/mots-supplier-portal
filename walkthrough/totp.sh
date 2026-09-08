#!/usr/bin/env bash
# Current six-digit code for admin@mots.local on THIS database.
#
# The secret is read from the database rather than pasted in here: it is regenerated every time the
# database is dropped, and a hard-coded copy is wrong within minutes of the next reset - which is
# exactly how two runs were lost to "invalid code" before this script read it live.
set -euo pipefail
cd "$(dirname "$0")/.."

SECRET=$(docker compose exec -T postgres psql -U postgres -d mots_supplier_portal -tAc \
  "SELECT encode(t.\"Value\"::bytea,'escape')
     FROM identity.user_token t
     JOIN identity.app_user u ON u.\"Id\" = t.\"UserId\"
    WHERE u.\"Email\" = 'admin@mots.local' AND t.\"Name\" = 'AuthenticatorKey';" | tr -d '[:space:]')

if [ -z "$SECRET" ]; then
  echo "No authenticator key for admin@mots.local - has the API started against this database yet?" >&2
  exit 1
fi

python3 -c "
import hmac,hashlib,struct,time,base64,sys
s=sys.argv[1]
k=base64.b32decode(s+'='*(-len(s)%8))
h=hmac.new(k,struct.pack('>Q',int(time.time())//30),hashlib.sha1).digest()
o=h[19]&0xf
print('%06d'%((struct.unpack('>I',h[o:o+4])[0]&0x7fffffff)%1000000))" "$SECRET"
