#!/usr/bin/env bash
# Current TOTP code for admin@mots.local on this database.
# The secret is regenerated whenever the database is dropped - take it from the API start-up log.
python3 -c "
import hmac,hashlib,struct,time,base64
s='4D4MCXUJB5R2A5M7JC4SNNIHZ2UCMWM5'
k=base64.b32decode(s+'='*(-len(s)%8))
h=hmac.new(k,struct.pack('>Q',int(time.time())//30),hashlib.sha1).digest()
o=h[19]&0xf
print('%06d'%((struct.unpack('>I',h[o:o+4])[0]&0x7fffffff)%1000000))"
