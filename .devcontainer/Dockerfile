ARG VARIANT="10.0"
FROM mcr.microsoft.com/devcontainers/dotnet:1-${VARIANT}

# [Optional] Install additional OS packages.
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends make
