# Microsoft alpine used for fetching dotnet dependencies automatically.
FROM mcr.microsoft.com/dotnet/core/runtime-deps:3.1-alpine3.11

RUN apk add bash libc6-compat ttf-dejavu --repository=http://dl-cdn.alpinelinux.org/alpine/edge/testing/

COPY /app/out /app
COPY /public /app/public

WORKDIR /app

RUN chmod +x ./ServicePixelStreamingOrchestrator

EXPOSE 8080

ENTRYPOINT ["./ServicePixelStreamingOrchestrator"]