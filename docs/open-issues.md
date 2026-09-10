# Open issues

- `CodexProvider.SparkDefinition` (`codex.spark`) is still registered via reflection (`ProviderMetadataCatalog` scans all static `ProviderDefinition` properties) but `CHANGELOG.md` says the card is no longer generated. Dead definition should be removed or the registration filter tightened.
