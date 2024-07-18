FROM node:22 as build

WORKDIR /app
RUN corepack enable

COPY . .
RUN yarn install --immutable
RUN yarn run build

FROM nginx:alpine

COPY --from=build /app/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html

# Expose the default nginx port
EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
