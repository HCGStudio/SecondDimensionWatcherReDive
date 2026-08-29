'use strict';

globalThis.sdwPlugin = {
  handlers: {
    sendNotification(notification, configuration) {
      if (!configuration.url) throw new Error('Webhook configuration requires url.');
      const response = sdw.request('network.request', {
        method: 'POST',
        url: configuration.url,
        contentType: 'application/json',
        body: JSON.stringify(notification),
      });
      return { success: response.status >= 200 && response.status < 300 };
    },
  },
};
