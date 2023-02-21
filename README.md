# TelegramAI

Creates a Telegram bot to communicate with OpenAI via audio.

## How to create a Telegram bot

From Telegram, contact `@BotFather`

`/newbot`
`<name your bot>`
`<write your unique Telegram handle for the bot>`

Now copy the bot auth key returned.

```bash
pulumi config set azure-native:location "West Europe"
pulumi config set bot-handle "<your bot handle from above>"
pulumi config set --secret bot-key "<your bot key returned by @BotFather>"
pulumi config set openai-url "<your OpenAI URL>"
pulumi config set --secret openai-key "<your OpenAI key>"
pulumi up
# Currently this line won't work as the trigger API invoke args do not take the trigger name, getting URL from the LA in the portal
curl -X POST -H "Content-Type: application/json" -d "{\"url\":\"$(pulumi stack output WorkflowTriggerUrl --show-secrets)\", \"allowed_updates\":[\"message\"]}" "https://api.telegram.org/bot$(pulumi config get bot-key)/setWebhook"
```

# How to use it

Forward any audio message in a conversation to your bot and it will respond with the completion.

# TODO

* Use secret_token in webhook (https://core.telegram.org/bots/api#setwebhook)
* ChatGPT-like maintain conversation session
* Parameterize Cognitive service URL in workflow
* Paramterize TTS voice configuration