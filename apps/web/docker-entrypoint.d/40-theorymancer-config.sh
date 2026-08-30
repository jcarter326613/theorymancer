#!/bin/sh
set -eu

envsubst '${API_URL}' \
    < /etc/theorymancer/config.template.js \
    > /usr/share/nginx/html/config.js
