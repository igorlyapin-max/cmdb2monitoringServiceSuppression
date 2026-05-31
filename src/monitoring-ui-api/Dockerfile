FROM node:22-alpine AS runtime
RUN addgroup -S app && adduser -S app -G app
WORKDIR /app
COPY src/monitoring-ui-api/package*.json ./
RUN npm install --omit=dev --package-lock=false
COPY src/monitoring-ui-api ./
COPY rules /app/rules
COPY state/conversion-config /app/state/conversion-config
RUN chown -R app:app /app
USER app
ENV NODE_ENV=Production
ENV MONITORING_UI_HOST=0.0.0.0
ENV MONITORING_UI_PORT=8091
EXPOSE 8091
CMD ["node", "server.mjs"]
