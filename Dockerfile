# The image carries a musl build so it runs on Alpine, the usual base of a CI job. Git is a
# real dependency: the harness reads tracked evidence rather than the working tree.
FROM alpine:3.21

ARG TARGETARCH

RUN apk add --no-cache git ca-certificates

COPY image/${TARGETARCH}/harness /usr/local/bin/harness

ENTRYPOINT ["/usr/local/bin/harness"]
CMD ["check"]
