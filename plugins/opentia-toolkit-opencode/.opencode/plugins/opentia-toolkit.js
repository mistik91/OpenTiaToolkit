export const OpenTiaToolkitPlugin = async ({ client }) => {
  await client.app
    .log({
      body: {
        service: "opentia-toolkit",
        level: "info",
        message: "OpenTiaToolkit OpenCode plugin loaded",
      },
    })
    .catch(() => {})

  return {
    "shell.env": async (_input, output) => {
      output.env.OPEN_TIA_MCP_LOG_LEVEL ??= "Error"
    },
  }
}
