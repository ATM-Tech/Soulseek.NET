#!/bin/bash
set -e
__dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
__branch=$(git branch --no-color | grep -E '^\*' | awk '{print $2}')

if [ "${CIRCLECI}" = "true" ]; then
    __branch="${CIRCLE_BRANCH}"
fi

dotnet-sonarscanner begin /key:"jpdillingham_Soulseek.NET" /o:jpdillingham-github /d:sonar.host.url="https://sonarcloud.io" /d:sonar.exclusions="**/*examples*/**" /d:sonar.branch.name=${__branch} /d:sonar.login="${SONARCLOUD_TOKEN}" /d:sonar.cs.opencover.reportsPaths="tests/opencover.xml"

. "${__dir}/build.sh"
. "${__dir}/test.sh"

dotnet-sonarscanner end /d:sonar.login="${SONARCLOUD_TOKEN}"

bash <(curl -s https://codecov.io/bash) -f tests/opencover.xml