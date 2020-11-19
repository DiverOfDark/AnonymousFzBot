FROM mcr.microsoft.com/dotnet/sdk:5.0 as net-builder

ARG IsProduction=false
ARG CiCommitName=local
ARG CiCommitHash=sha

WORKDIR /build
ADD . .
RUN dotnet restore

RUN dotnet publish --output out/ --configuration Release --runtime linux-x64 --self-contained true

FROM mcr.microsoft.com/dotnet/sdk:5.0
ENV TZ=Europe/Moscow
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone

WORKDIR /app
COPY --from=net-builder /build/out ./net

ADD run.sh .
RUN chmod +x run.sh

ARG Token=
ENV Token=$Token

ENTRYPOINT ["/app/run.sh"]
