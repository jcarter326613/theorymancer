#!/bin/sh
set -eu

envsubst '${API_URL} ${FIREBASE_API_KEY} ${FIREBASE_AUTH_DOMAIN} ${FIREBASE_PROJECT_ID} ${FIREBASE_APP_ID} ${FIREBASE_TENANT_ID}' \
    < /etc/theorymancer/config.template.js \
    > /usr/share/nginx/html/config.js
