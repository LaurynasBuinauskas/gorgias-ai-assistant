// Maps the ISO code Gorgias reports (e.g. "de", "pt-BR") to a display name, so the panel
// can label the one-tap "Translate to …" action. Unknown codes fall back to upper-case.

const NAMES: Record<string, string> = {
  en: 'English',
  de: 'German',
  fr: 'French',
  es: 'Spanish',
  it: 'Italian',
  nl: 'Dutch',
  pt: 'Portuguese',
  pl: 'Polish',
  sv: 'Swedish',
  da: 'Danish',
  no: 'Norwegian',
  fi: 'Finnish',
  ru: 'Russian',
  cs: 'Czech',
  tr: 'Turkish',
  ja: 'Japanese',
  zh: 'Chinese',
  ko: 'Korean',
  ar: 'Arabic',
};

/** Base subtag, e.g. "pt-BR" → "pt". */
function base(code: string): string {
  return code.toLowerCase().split('-')[0] ?? code.toLowerCase();
}

export function languageName(code: string | null | undefined): string | null {
  if (!code) return null;
  return NAMES[base(code)] ?? code.toUpperCase();
}

export function isEnglish(code: string | null | undefined): boolean {
  return !!code && base(code) === 'en';
}
