FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/cmdbaggregation2cmdbuild/cmdbaggregation2cmdbuild.csproj /p:NuGetAudit=false
RUN dotnet publish src/cmdbaggregation2cmdbuild/cmdbaggregation2cmdbuild.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false /p:NuGetAudit=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN addgroup -S app && adduser -S app -G app
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/rules /app/rules
COPY --from=build /src/state/conversion-config /app/state/conversion-config
COPY --from=build /src/config /app/config
RUN mkdir -p /app/state && chown -R app:app /app
USER app
ENV ASPNETCORE_URLS=http://+:5181
EXPOSE 5181
ENTRYPOINT ["dotnet", "cmdbaggregation2cmdbuild.dll"]
