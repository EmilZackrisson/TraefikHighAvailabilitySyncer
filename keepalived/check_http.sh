#!/bin/bash

curl -s --max-time 2 http://syncer:8080 > /dev/null
if [ $? -ne 0 ]; then
    exit 1
fi
exit 0
