FROM node:22-bookworm
WORKDIR /app

ARG VITE_API_BASE_URL=http://localhost:8081
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
ENV npm_config_registry=https://registry.npmjs.org/
ENV npm_config_audit=false
ENV npm_config_fund=false

COPY package.json .npmrc ./
RUN npm install --no-audit --no-fund

COPY . .

EXPOSE 5173
CMD ["npm","run","dev","--","--host","0.0.0.0","--port","5173"]
