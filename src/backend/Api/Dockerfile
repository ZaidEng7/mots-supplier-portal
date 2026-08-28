FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore MotsSupplierPortal.slnx
RUN dotnet publish Api/MotsSupplierPortal.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# docker:S6471 - run as an unprivileged user. Without this the API runs as root inside the
# container, so a container escape or an RCE in the API starts with root rather than a normal
# account. The aspnet base image already ships a non-root `app` user (uid 1654); using it avoids
# creating a redundant one.
#
# Ownership is set on /app so the runtime can read its own assemblies. The app writes nothing to
# the image filesystem - uploads go to object storage via IFileStorage, logs go to stdout - so no
# writable mount is required here. If that ever changes, the target path needs explicit ownership
# or a volume, because a non-root user cannot write to root-owned directories.
RUN chown -R app:app /app
USER app

# >1024 so the unprivileged user can bind it without NET_BIND_SERVICE.
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "MotsSupplierPortal.Api.dll"]
