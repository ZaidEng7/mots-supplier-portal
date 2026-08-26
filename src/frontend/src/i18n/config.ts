import i18n from 'i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import { initReactI18next } from 'react-i18next'

const resources = {
  ar: {
    translation: {
      appName: 'بوابة الموردين',
      nav: { home: 'الرئيسية' },
      health: { title: 'حالة النظام', healthy: 'يعمل بشكل طبيعي', unhealthy: 'غير متاح' },
      reference: { currencies: 'العملات' },
      errors: { notFound: 'الصفحة غير موجودة', forbidden: 'غير مصرح', serverError: 'خطأ في الخادم' },
      language: 'English',
    },
  },
  en: {
    translation: {
      appName: 'Supplier Portal',
      nav: { home: 'Home' },
      health: { title: 'System status', healthy: 'Healthy', unhealthy: 'Unavailable' },
      reference: { currencies: 'Currencies' },
      errors: { notFound: 'Page not found', forbidden: 'Forbidden', serverError: 'Server error' },
      language: 'العربية',
    },
  },
}

export const RTL_LANGUAGES = new Set(['ar'])

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources,
    fallbackLng: 'ar',
    supportedLngs: ['ar', 'en'],
    interpolation: { escapeValue: false },
  })

export default i18n
