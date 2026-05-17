# i18n Translation Management Guide

## Project Structure
```
BotManager.Frontend/
  src/
    i18n/
      cs.json     <- Czech translations
      en.json     <- English translations
  .i18nrc.json    <- Configuration for VSCode i18n extensions
```

## How to Edit Translations

### Method 1: VSCode i18n Extension (Recommended)
With the refactored structure, VSCode i18n extensions now work properly:
1. Install an i18n extension (e.g., "i18n Ally" by Lokalise)
2. Hover over any `i18n.t('key')` call in templates
3. The extension shows the current translation and lets you edit it
4. Files auto-save

### Method 2: Direct File Editing
1. Open the JSON files:
   - `src/i18n/cs.json` for Czech
   - `src/i18n/en.json` for English
2. Use Find & Replace (Ctrl+H) to:
   - Find translation keys: `"nav.dashboard": "`
   - Replace the value (everything after the colon)
3. Both languages update independently

### Method 3: Key Organization
Translation keys follow this naming convention:
- `nav.*` - Navigation menu items
- `home.*` - Home/public pages
- `login.*` - Login page
- `admin.*` - Admin dashboard
- `bot.*` - Bot detail page (properties, boards, config)
- `logs.*` - System logs page
- `config.*` - System configuration page
- `commands.*` - Global commands page
- `footer.*` - Footer components
- `status.*` - Status values

### Build & Deployment
- **Development**: `npm run build` → outputs to `dist/bot-manager.frontend/browser/i18n/`
- **Dev Server**: `ng serve` → JSON files served from `src/i18n/`
- Angular's HttpClient automatically loads from `/i18n/{lang}.json`
- Fallback to hardcoded translations if JSON load fails

### Adding New Translations
1. Add the key-value pair to both `cs.json` and `en.json`:
   ```json
   "feature.label": "Your translation here"
   ```
2. Use in templates:
   ```html
   {{ i18n.t('feature.label') }}
   ```
3. No service changes needed - JSON files are automatically loaded

### Supported Languages
Currently configured languages: Czech (`cs`), English (`en`)
Default language: Czech
To add more languages:
1. Create `src/i18n/{lang}.json` with translations
2. Update `I18nService.ts` - add to `AppLang` type
3. Update `.i18nrc.json` - add language to `locales` array
