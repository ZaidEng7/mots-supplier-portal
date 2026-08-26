import i18n from 'i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import { initReactI18next } from 'react-i18next'

const resources = {
  ar: {
    translation: {
      appName: 'بوابة الموردين',
      nav: { home: 'الرئيسية', dashboard: 'لوحة التحكم', backOffice: 'الإدارة الداخلية', logout: 'تسجيل الخروج' },
      health: { title: 'حالة النظام', healthy: 'يعمل بشكل طبيعي', unhealthy: 'غير متاح' },
      reference: { currencies: 'العملات' },
      errors: { notFound: 'الصفحة غير موجودة', forbidden: 'غير مصرح', serverError: 'خطأ في الخادم' },
      auth: {
        loginTitle: 'تسجيل الدخول',
        email: 'البريد الإلكتروني',
        password: 'كلمة المرور',
        submit: 'دخول',
        forgotPassword: 'نسيت كلمة المرور؟',
        loginFailed: 'بيانات الدخول غير صحيحة',
        emailNotVerified: 'يرجى تفعيل بريدك الإلكتروني أولاً',
        lockedOut: 'الحساب مقفل مؤقتاً بسبب محاولات فاشلة متكررة',
        forgotTitle: 'إعادة تعيين كلمة المرور',
        forgotSubmit: 'إرسال رابط إعادة التعيين',
        forgotSent: 'إذا كان الحساب موجوداً، تم إرسال رسالة بريد إلكتروني',
        resetTitle: 'تعيين كلمة مرور جديدة',
        newPassword: 'كلمة المرور الجديدة',
        resetSubmit: 'إعادة تعيين',
        resetSuccess: 'تم تعيين كلمة المرور بنجاح، يمكنك الآن تسجيل الدخول',
        resetInvalid: 'الرابط غير صالح أو منتهي الصلاحية',
        verifyingEmail: 'جاري تفعيل البريد الإلكتروني...',
        verifySuccess: 'تم تفعيل بريدك الإلكتروني بنجاح',
        verifyFailed: 'تعذر تفعيل البريد الإلكتروني',
      },
      dashboard: {
        welcome: 'مرحباً، {{email}}',
        supplierId: 'رقم المورد',
        permission: 'الصلاحية الحالية',
        placeholder: 'سيتم عرض ملخص الطلبات والعقود هنا لاحقاً.',
      },
      language: 'English',
    },
  },
  en: {
    translation: {
      appName: 'Supplier Portal',
      nav: { home: 'Home', dashboard: 'Dashboard', backOffice: 'Back Office', logout: 'Log out' },
      health: { title: 'System status', healthy: 'Healthy', unhealthy: 'Unavailable' },
      reference: { currencies: 'Currencies' },
      errors: { notFound: 'Page not found', forbidden: 'Forbidden', serverError: 'Server error' },
      auth: {
        loginTitle: 'Sign in',
        email: 'Email',
        password: 'Password',
        submit: 'Sign in',
        forgotPassword: 'Forgot password?',
        loginFailed: 'Invalid email or password',
        emailNotVerified: 'Please verify your email first',
        lockedOut: 'Account is temporarily locked after repeated failed attempts',
        forgotTitle: 'Reset your password',
        forgotSubmit: 'Send reset link',
        forgotSent: 'If that account exists, a reset email has been sent',
        resetTitle: 'Set a new password',
        newPassword: 'New password',
        resetSubmit: 'Reset password',
        resetSuccess: 'Password reset. You can now sign in.',
        resetInvalid: 'This link is invalid or has expired',
        verifyingEmail: 'Verifying your email...',
        verifySuccess: 'Your email has been verified',
        verifyFailed: 'Could not verify this email',
      },
      dashboard: {
        welcome: 'Welcome, {{email}}',
        supplierId: 'Supplier ID',
        permission: 'Current permission',
        placeholder: 'Order and contract summaries will appear here.',
      },
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
