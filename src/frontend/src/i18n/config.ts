import i18n from 'i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import { initReactI18next } from 'react-i18next'

const resources = {
  ar: {
    translation: {
      appName: 'بوابة الموردين',
      nav: { home: 'الرئيسية', dashboard: 'لوحة التحكم', onboarding: 'استكمال الملف', offerings: 'الخدمات المعروضة', team: 'الفريق', settings: 'الإعدادات', backOffice: 'الإدارة الداخلية', logout: 'تسجيل الخروج', mobileTabBarLabel: 'التنقل الرئيسي', rfqs: 'طلبات العروض' },
      // SCR-900. UX-WRITING.md §4's empty-state formula: title (what this is) + one line (why it
      // is empty). §4's table has NO row for a notification centre, so this copy is DRAFTED, not
      // transcribed - reported as a documentation gap rather than presented as approved.
      // SCR-500. The empty state is transcribed from UX-WRITING.md §4's own row for this persona:
      // "Evaluator — nothing assigned | 'Nothing to evaluate' | 'Proposals assigned to you for
      // scoring will appear here.' | —" (no action, per the table's own dash).
      // SCR-400 §10, SCR-401, SCR-300 (FR-DSH-002). KPI labels are drafted: the specs name the
      // tiles in English prose and no Arabic copy exists for them in UX-WRITING.
      // ═══════════════════════════════════════════════════════════════════════════════════════
      // EPIC-16 · ARABIC FOR REVIEW (part 2 of 2; part 1 is `status.invitation` above).
      //
      //   [§7-style drafted]  authored here in the style of §7's tables - NOT YET APPROVED
      //   [reused]            already approved elsewhere in this file, repeated for consistency
      //
      // InvitationStatus has no §7 table - five members that ship on the wire (§12.4's
      // invitationStatus) and render as chips with nothing to say. Drafted below in §7's register:
      // professional MSA, authored rather than translated, gender agreeing with the subject (الدعوة
      // is feminine, which is why Declined is not «مرفوض»).
      // ═══════════════════════════════════════════════════════════════════════════════════════
      supplierDashboard: {
        title: 'لوحة المورد',                     // [§7-style drafted]
        greeting: 'أهلاً بك، {{name}}',          // [§7-style drafted]
        kpis: {
          openInvitations: 'دعوات مفتوحة',        // [§7-style drafted]
          draftProposals: 'عروض مسودة',           // [§7-style drafted]
          submittedProposals: 'عروض مُقدَّمة',      // [§7-style drafted] reuses §7.4's «مُقدَّم» as an adjective
          documentsNeedingAttention: 'وثائق بحاجة إلى إجراء', // [§7-style drafted]
        },
        actionRequired: {
          title: 'يتطلب إجراءً',                                        // [§7-style drafted]
          expiringDocuments: 'وثائق تقترب من الانتهاء ({{count}})',      // [§7-style drafted]
          rejectedDocuments: 'وثائق مرفوضة ({{count}})',                 // [§7-style drafted]
          invitationsClosingSoon: 'دعوات تُغلق قريباً ({{count}})',        // [§7-style drafted]
          clarificationsAnswered: 'استيضاحات تمت الإجابة عليها ({{count}})', // [§7-style drafted] §8's glossary term
          awardOffers: 'عروض ترسية ({{count}})',                         // [§7-style drafted] §8's «ترسية»
          dismiss: 'إخفاء',                                              // [§7-style drafted]
        },
        invitations: 'الدعوات والمواعيد',          // [§7-style drafted]
        proposals: 'عروضي',                        // [§7-style drafted]
        validUntil: 'سارٍ حتى {{date}}',            // [§7-style drafted]
        noValidity: 'لا يوجد تاريخ سريان',          // [§7-style drafted]
        profileHealth: 'اكتمال الملف والوثائق',      // [§7-style drafted]
        completeness: 'اكتمال الوثائق المطلوبة: {{done}} من {{total}}', // [§7-style drafted]
        nextDocument: 'الوثيقة التالية المطلوبة: {{code}}',              // [§7-style drafted]
        allDocuments: 'اكتملت جميع الوثائق المطلوبة.',                   // [§7-style drafted]
        notifications: 'آخر الإشعارات',            // [§7-style drafted]
        openNotifications: 'عرض الإشعارات',        // [reused] wording of procurementDashboard.openNotifications
        // §1's not-yet-approved state. Deliberately not an empty dashboard - see the report.
        pendingTitle: 'طلب التسجيل قيد المراجعة',   // [§7-style drafted]
        pendingBody: 'سنُعلمك فور اعتماد ملفكم. يمكنك متابعة استكمال بياناتكم في هذه الأثناء.', // [§7-style drafted]
        pendingCta: 'متابعة استكمال الملف',         // [§7-style drafted]
        erpDegraded: 'مزامنة أمر الشراء متوقفة مؤقتاً. لا يؤثر ذلك على عرضكم.', // [§7-style drafted] §9's sync tone
        emptyTitle: 'لا توجد دعوات بعد',            // [reused] §4's «لا توجد عروض بعد» pattern
        emptyBody: 'ستظهر هنا دعوات طلبات عروض الأسعار عند دعوتكم للمشاركة.', // [§7-style drafted]
        loadFailed: 'تعذر تحميل هذا القسم',         // [§7-style drafted] per-widget, not per-page
        retry: 'إعادة المحاولة',                    // [reused]
      },
      // FEAT-19.1/19.2 report screen. AUTHORED, not transcribed: no document specifies this
      // screen at all, so every string here is an invention and none of it is a §7 label set. The
      // Arabic is written to match the register of the screens around it and needs a native review
      // before it ships - flagged rather than presented as settled.
      //
      // D-18: three strings here diverged from their counterparts in ReportViews.cs, which produces
      // the PDF/CSV. The rule chosen is IDENTICAL, not "screen terse / export self-describing" -
      // both surfaces render the same table with the same column headers, so there is no context the
      // export lacks, and all three divergences turned out to be errors rather than adaptations.
      // See DECISIONS-TAKEN.md.
      // ── EPIC-18/SCR-600 · ARABIC FOR REVIEW ─────────────────────────────────────────
      // §7 has no notification-template table. Drafted in §7's register - NOT YET APPROVED.
      notificationTemplates: {
        title: 'قوالب الإشعارات',                            // [§7-style drafted]
        subtitle: 'صياغة الإشعارات لكل نوع بالعربية والإنجليزية. النوع غير المعدّل يستخدم الصياغة الأصلية.', // [§7-style drafted]
        shipped: 'الصياغة الأصلية',                          // [§7-style drafted]
        overridden: 'معدّل',                                 // [reused] matches systemSettings
        overriddenAt: 'عُدّل في {{at}}',                      // [reused] matches systemSettings
        edit: 'تعديل',                                       // [reused]
        collapse: 'إخفاء',                                   // [reused]
        titleAr: 'العنوان (عربي)',                           // [reused] §7's own term
        titleEn: 'العنوان (إنجليزي)',                        // [reused] §7's own term
        bodyAr: 'النص (عربي)',                               // [§7-style drafted]
        bodyEn: 'النص (إنجليزي)',                            // [§7-style drafted]
        tokens: 'الرموز المتاحة: {{tokens}}',                 // [§7-style drafted]
        noTokens: 'لا رموز متاحة لهذا النوع.',                // [§7-style drafted]
        shippedCopy: 'الصياغة الأصلية (ما سيُستعاد)',          // [§7-style drafted]
        save: 'حفظ',                                         // [reused]
        saved: 'تم حفظ الصياغة',                             // [§7-style drafted]
        revert: 'استعادة الصياغة الأصلية',                    // [§7-style drafted]
        reverted: 'تمت استعادة الصياغة الأصلية',              // [§7-style drafted]
        loadFailed: 'تعذّر تحميل القوالب',                    // [§7-style drafted]
        retry: 'إعادة المحاولة',                             // [reused]
        errors: {
          unknownTokens: 'رموز غير متاحة لهذا النوع: {{tokens}}', // [§7-style drafted]
          saveFailed: 'تعذّر حفظ الصياغة',                    // [§7-style drafted]
          revertFailed: 'تعذّرت الاستعادة',                   // [§7-style drafted]
        },
      },
      // §7 has no settings table. Drafted in §7's register - NOT YET APPROVED, in ARABIC-REVIEW.md.
      systemSettings: {
        title: 'إعدادات النظام',                            // [§7-style drafted]
        subtitle: 'قيم تسري على النظام بأكمله. الإعداد غير المعدّل يعمل بالقيمة الافتراضية.', // [§7-style drafted]
        value: 'القيمة',                                    // [reused] §7's own term
        save: 'حفظ',                                        // [reused]
        saved: 'تم حفظ الإعداد',                            // [§7-style drafted]
        overridden: 'معدّل',                                // [§7-style drafted]
        overriddenAt: 'عُدّل في {{at}}',                     // [§7-style drafted]
        usingDefault: 'القيمة الافتراضية ({{value}})',        // [§7-style drafted]
        loadFailed: 'تعذّر تحميل الإعدادات',                  // [§7-style drafted]
        retry: 'إعادة المحاولة',                             // [reused]
        keys: {
          'registration.mode': 'تسجيل الموردين',             // [§7-style drafted]
          'proposals.defaultCurrencyCode': 'العملة الافتراضية', // [§7-style drafted]
          'documents.expiringSoonWindowDays': 'مهلة التنبيه لقرب انتهاء المستند (أيام)', // [§7-style drafted]
          'documents.renewalReminderDays': 'مواعيد تذكير التجديد (أيام)', // [§7-style drafted]
        },
        help: {
          'registration.mode': 'عند الإغلاق يُرفض التسجيل الذاتي ويُطلب من المتقدّم التواصل مع الوزارة.', // [§7-style drafted]
          'proposals.defaultCurrencyCode': 'تُستخدم كقيمة أولية في نماذج العروض. يجب أن تكون عملة مفعّلة.', // [§7-style drafted]
          'documents.expiringSoonWindowDays': 'تحدّد متى تنتقل حالة المستند إلى «قارب على الانتهاء»، وهي مستقلة عن مواعيد التذكير.', // [§7-style drafted]
          'documents.renewalReminderDays': 'قائمة أيام قبل الانتهاء يُرسل عندها تذكير. لا تكرار.', // [§7-style drafted]
        },
        choices: {
          'registration.mode': {
            open: 'مفتوح للتسجيل الذاتي',                    // [§7-style drafted]
            closed: 'مغلق',                                 // [§7-style drafted]
          },
        },
        hints: {
          integerList: 'أرقام مفصولة بفواصل، مثال: 30,14,3',   // [§7-style drafted]
          range: 'من {{min}} إلى {{max}}',                   // [§7-style drafted]
        },
        errors: {
          value_required: 'القيمة مطلوبة',                    // [reused] §4's required pattern
          value_not_allowed: 'قيمة غير مسموحة',               // [§7-style drafted]
          value_out_of_range: 'القيمة خارج النطاق المسموح',    // [§7-style drafted]
          value_has_duplicates: 'لا يمكن تكرار الرقم نفسه',    // [§7-style drafted]
          reference_code_not_active: 'الرمز غير موجود أو غير مفعّل', // [§7-style drafted]
          unknown: 'تعذّر حفظ الإعداد',                       // [§7-style drafted]
        },
      },
      // §7 has no admin-dashboard table either. Drafted in §7's register - NOT YET APPROVED, in
      // ARABIC-REVIEW.md's pile.
      adminOverview: {
        title: 'لوحة إدارة النظام',                        // [§7-style drafted]
        kpis: {
          users: 'المستخدمون',                             // [reused] §7's own term
          roles: 'الأدوار',                                // [reused] matches roleManagement
          outboxPending: 'رسائل قيد الإرسال',               // [§7-style drafted]
          auditRows: 'سجلات التدقيق (24 ساعة)',            // [§7-style drafted]
        },
        outbox: 'قائمة الإرسال',                           // [§7-style drafted]
        outboxPending: 'قيد الإرسال',                      // [§7-style drafted]
        outboxFailed: 'فاشلة',                             // [reused] §4's failure wording
        outboxOldest: 'عمر أقدم رسالة قيد الإرسال',        // [§7-style drafted]
        outboxDrained: 'لا توجد رسائل معلّقة',              // [§7-style drafted]
        outboxFailedWarning: 'توجد رسائل فاشلة تحتاج مراجعة', // [§7-style drafted]
        erpNotConfigured: 'لا يوجد ربط فعلي بنظام ERP',           // [drafted] B-1/BRULE-011
        erpNotConfiguredBody: 'تُسجَّل الرسائل في السجل ولا تُرسل إلى أي نظام خارجي. لا تُعتبر الرسائل المُرسلة دليلاً على وصول البيانات.', // [drafted]
        minutes: '{{value}} دقيقة',                        // [§7-style drafted]
        jobs: 'المهام المجدولة',                            // [§7-style drafted]
        jobsDisabled: 'المهام المجدولة معطّلة',              // [§7-style drafted]
        jobsDisabledBody: 'لن تُرسل التذكيرات ولن تُغلق الطلبات تلقائياً حتى تُفعّل المهام المجدولة في إعدادات النشر.', // [§7-style drafted]
        jobsMissing: 'مهام مفقودة من التسجيل',              // [§7-style drafted]
        jobsHealthy: '{{value}} مهام مسجّلة',               // [§7-style drafted]
        referenceData: 'البيانات المرجعية',                 // [reused] §7's own term
        referenceEmpty: 'جدول مرجعي بلا رموز مفعّلة - سيتعذّر التسجيل', // [§7-style drafted]
        activeOfTotal: '{{active}} من {{total}}',           // [§7-style drafted]
        tables: {
          categories: 'التصنيفات',                         // [reused] §7's own term
          'document-types': 'أنواع المستندات',              // [reused] §7's own term
          currencies: 'العملات',                           // [reused] matches reference.currencies
          'units-of-measure': 'وحدات القياس',               // [reused] §7's own term
          regions: 'المناطق',                              // [reused] §7's own term
        },
        loadFailed: 'تعذّر تحميل لوحة إدارة النظام',        // [§7-style drafted]
        retry: 'إعادة المحاولة',                           // [reused]
      },
      // §7 has no ministry table. Drafted in §7's register - NOT YET APPROVED, in
      // ARABIC-REVIEW.md's pile.
      ministry: {
        title: 'لوحة الحوكمة',                          // [§7-style drafted]
        kpis: {
          suppliers: 'الموردون المسجلون',                // [§7-style drafted]
          rfqs: 'طلبات عروض الأسعار',                    // [reused] §7's own term
          awards: 'الترسيات',                            // [reused] §8's «ترسية»
          participation: 'متوسط العروض لكل طلب',          // [§7-style drafted]
        },
        awardedValue: 'إجمالي قيمة الترسيات',            // [§7-style drafted]
        commercialWithheld: 'القيم المالية غير معروضة',   // [§7-style drafted]
        commercialWithheldBody: 'وفق سياسة الاطلاع الحالية، تُعرض المؤشرات المجمّعة دون القيم المالية.', // [§7-style drafted]
        suppliersByState: 'الموردون حسب حالة دورة الحياة', // [reused] matches the compliance report (D-18)
        rfqsByState: 'طلبات عروض الأسعار حسب الحالة',     // [reused] matches the procurement report
        empty: 'لا توجد بيانات بعد',                     // [reused] §4's empty-state pattern
        loadFailed: 'تعذّر تحميل لوحة الحوكمة',           // [§7-style drafted]
        retry: 'إعادة المحاولة',                         // [reused]
      },
      reports: {
        title: 'التقارير',
        from: 'من',
        to: 'إلى',
        state: 'الحالة',
        count: 'العدد',
        interval: 'الفترة',
        sampleSize: 'عدد الطلبات المقيسة',
        medianHours: 'الوسيط بالساعات',
        notMeasured: '(غير مقيس)',              // D-18: parenthesised, matching the export
        noRows: 'لا توجد بيانات',
        exportPdf: 'تصدير PDF',
        exportCsv: 'تصدير CSV',
        loadFailed: 'تعذّر تحميل التقرير.',
        downloadFailed: 'تعذّر تنزيل الملف.',
        retry: 'إعادة المحاولة',
        intervals: {
          DraftToReview: 'من المسودة إلى المراجعة',
          ReviewToApproved: 'من المراجعة إلى الاعتماد',
          ApprovedToPublished: 'من الاعتماد إلى النشر',
          PublishedToSubmissionClosed: 'من النشر إلى إغلاق التقديم',
          SubmissionClosedToEvaluation: 'من إغلاق التقديم إلى التقييم',
          EvaluationToAward: 'من التقييم إلى الترسية',
        },
        procurement: {
          title: 'تقرير المشتريات',
          rfqsByState: 'طلبات عروض الأسعار حسب الحالة',
          cycleTime: 'زمن الدورة',
          awardsByState: 'الترسيات حسب الحالة',
          coverageFloor: 'تُقاس أزمنة الدورة من {{date}} فصاعداً؛ الطلبات الأقدم غير مشمولة.',
          coverageNone: 'لا توجد انتقالات مسجَّلة بعد، لذلك لا يمكن قياس زمن الدورة.',
        },
        compliance: {
          title: 'تقرير الامتثال',
          suppliersByState: 'الموردون حسب حالة دورة الحياة', // D-18: matches the export; this groups by LifecycleState
          documentsByState: 'المستندات حسب الحالة (أحدث الإصدارات)',
          registryScope: 'تشمل هذه الأعداد جميع الموردين المسجَّلين، وليست مقصورة على جهتك.',
        },
      },
      procurementDashboard: {
        title: 'لوحة المشتريات',
        from: 'من', to: 'إلى',
        newRfq: 'طلب جديد',
        kpis: {
          activeRfqs: 'طلبات نشطة',
          closingThisWeek: 'تُغلق هذا الأسبوع',
          awaitingMyAction: 'بانتظار إجراء مني',
          pendingApprovals: 'اعتمادات معلّقة',
          awardsInProgress: 'ترسيات قيد الإنجاز',
        },
        pipeline: 'مسار الطلبات',
        tasks: 'المواعيد والمهام',
        taskKinds: {
          SubmissionClosing: 'إغلاق التقديم',
          EvaluationDue: 'موعد التقييم',
          RecommendationPending: 'توصية معلّقة',
        },
        noTasks: 'لا توجد مهام قادمة',
        activity: 'آخر النشاطات',
        openNotifications: 'عرض الإشعارات',
        approvals: 'الاعتمادات',
        openApprovals: 'فتح قائمة الاعتمادات',
        emptyTitle: 'لا توجد طلبات بعد',
        emptyBody: 'أنشئ أول طلب عرض أسعار لدعوة الموردين.',
        loadFailed: 'تعذر تحميل اللوحة',
        retry: 'إعادة المحاولة',
      },
      approvals: {
        title: 'الاعتمادات',
        // Not a personal queue: nothing resolves a single named approver, so the copy says "your
        // organization" rather than "assigned to you".
        subtitle: 'الأعمال التي تنتظر اعتماداً في جهتك.',
        rfqQueue: 'طلبات بانتظار الاعتماد للنشر',
        awardQueue: 'ترسيات بانتظار الاعتماد',
        noRfqs: 'لا توجد طلبات بانتظار الاعتماد',
        noAwards: 'لا توجد ترسيات بانتظار الاعتماد',
        loadFailed: 'تعذر تحميل الاعتمادات',
        retry: 'إعادة المحاولة',
      },
      reviewDashboard: {
        title: 'لوحة مراجعة التسجيل',
        openQueue: 'فتح قائمة المراجعة',
        kpis: {
          pending: 'بانتظار المراجعة',
          underReview: 'قيد المراجعة',
          infoRequested: 'مطلوب معلومات',
          unassigned: 'غير مُسندة',
          assignedToMe: 'مُسندة إليّ',
        },
        aging: 'مدة الانتظار',
        // A duration, never a breach - no document defines a review SLA.
        oldestCase: 'أقدم طلب مفتوح ينتظر منذ {{days}} يوماً.',
        noOpenCases: 'لا توجد طلبات مفتوحة.',
        watchlist: 'وثائق تقترب من الانتهاء',
        noExpiring: 'لا توجد وثائق تقترب من الانتهاء',
        loadFailed: 'تعذر تحميل اللوحة',
        retry: 'إعادة المحاولة',
      },
      evaluationDashboard: {
        title: 'تقييماتي',
        tabs: { Assigned: 'مُسندة', InProgress: 'قيد التنفيذ', Submitted: 'مُرسلة' },
        emptyTitle: 'لا يوجد ما يُقيَّم',
        emptyBody: 'ستظهر هنا العروض المسندة إليك للتقييم.',
        progress: 'أُنجز {{done}} من {{total}}',
        due: 'الموعد المستهدف: {{date}}',
        noDueDate: 'لا يوجد موعد مستهدف',
        score: 'ابدأ التقييم',
        review: 'عرض التقييم',
        loadFailed: 'تعذر تحميل التقييمات',
        retry: 'إعادة المحاولة',
      },
      notifications: {
        title: 'الإشعارات',
        emptyTitle: 'لا توجد إشعارات بعد',
        emptyBody: 'ستظهر هنا التحديثات المتعلقة بطلبات عروض الأسعار وعروضك والترسيات.',
        markAllRead: 'تعليم الكل كمقروء',
        markRead: 'تعليم كمقروء',
        open: 'فتح',
        loadFailed: 'تعذر تحميل الإشعارات',
        retry: 'إعادة المحاولة',
        bell: 'الإشعارات',
        bellWithCount: 'الإشعارات، {{count}} غير مقروء',
      },
      common: { loading: 'جاري التحميل...', cancel: 'إلغاء', concurrencyConflict: 'لم يتم الحفظ — تم تعديل هذا العنصر من قبل مستخدم آخر. يرجى إعادة التحميل والمحاولة مجدداً.' },
      // UX-WRITING.md §7 "Status labels (aligned to canonical state machines)" - transcribed
      // verbatim, not authored here. §7 is "the single source for chip text and for the accessible
      // name announced to screen readers", so these keys are the only place a domain state becomes
      // words. InvitationStatus has NO §7 table; EPIC-16 drafts its five labels in §7's style - see
      // the tagged review block above - because this screen renders them as chips.
      status: {
        // §7.1 "Supplier onboarding". The doc groups onboarding and lifecycle states in ONE
        // table; the code splits them across SupplierOnboardingState and SupplierLifecycleState.
        // Transcribed to the doc's grouping, so both code enums render through this one machine.
        // SupplierLifecycleState.None has no §7.1 row - reported, not authored.
        onboarding: {
          Draft: 'مسودة', EmailVerified: 'تم التحقق من البريد', ProfileInProgress: 'قيد الإكمال',
          Submitted: 'مُقدَّم', UnderReview: 'قيد المراجعة', InfoRequested: 'مطلوب معلومات',
          Resubmitted: 'أُعيد التقديم', Approved: 'معتمد', Rejected: 'مرفوض',
          Active: 'نشط', Suspended: 'موقوف', Deactivated: 'مُلغى التفعيل',
        },
        // §7.2 "Supplier document". Its "Required" row has no DocumentState member and is therefore
        // not transcribed; PendingScan and ScanRejected are members with no §7.2 row. Both
        // directions reported as documentation gaps.
        document: {
          // §7.2's first row. Not a DocumentState member - it is the resting label for a required
          // type with nothing uploaded yet, which SCREEN-SPECIFICATIONS §2 (SCR-106) also lists
          // first in its StatusBadge set: "(Required / Uploaded / UnderReview / Approved / Rejected)".
          Required: 'مطلوب',
          // Code-authored, no §7 row. What `Required` BECOMES once the supplier has attempted to
          // submit and this document is still absent - a validation display state, not a document
          // state. Carried over from the `onboarding.missing` key it replaces.
          Missing: 'ناقص',
          Uploaded: 'تم الرفع', UnderReview: 'قيد المراجعة', Approved: 'معتمد',
          Rejected: 'مرفوض', ExpiringSoon: 'ينتهي قريباً', Expired: 'منتهٍ',
          // Carried over verbatim from the pre-existing `onboarding.docState` namespace this
          // replaces - NOT authored here. §7.2 has no row for either; removing them would regress
          // the chip to raw English, so they are migrated and reported as a documentation gap.
          PendingScan: 'جاري الفحص', ScanRejected: 'مرفوض (فحص الفيروسات)',
        },
        // UX-WRITING.md §7.6 "Award / Approval & ERP sync". Three rows, transcribed. The enum's
        // fourth member, NotRequested, has NO entry on purpose: §7.6 has no row for it because
        // nothing has been asked of the ERP yet, so it is not a sync state - rendering it as
        // "pending" would tell a procurement officer a request is in flight when none was made.
        // Absence is the label. AwardPage renders no chip at all for it.
        erpSync: { Requested: 'بانتظار المزامنة', Synced: 'تمت المزامنة', Failed: 'فشل المزامنة' },
        // EPIC-16 · ARABIC FOR REVIEW (part 1 of 2; part 2 is the `supplierDashboard` block below).
        //
        // §7 has NO invitation table. These five ship on the wire (§12.4's invitationStatus) and
        // render as chips, so they are drafted here in §7's own register: professional MSA, authored
        // rather than translated, gender agreeing with the subject - الدعوة is feminine, which is why
        // Declined is not «مرفوض».
        //
        //   [§7-style drafted]  authored here - NOT YET APPROVED
        //   [reused]            already approved elsewhere in this file, kept identical
        invitation: {
          Invited: 'مدعو',           // [§7-style drafted] masculine; the subject is المورد
          Viewed: 'تمت المشاهدة',     // [§7-style drafted] §9's «تم الحفظ» construction
          Responding: 'قيد الرد',     // [§7-style drafted] mirrors §7.3's «قيد التقييم»
          Submitted: 'مُقدَّم',         // [reused] §7.4's Proposal:Submitted - same concept, same word
          Declined: 'معتذر عنها',     // [§7-style drafted] feminine; declining an invitation is اعتذار, not رفض
        },
        rfq: {
          Draft: 'مسودة', InternalReview: 'مراجعة داخلية', Approved: 'معتمد', Published: 'منشور',
          SubmissionOpen: 'مفتوح للتقديم', SubmissionClosed: 'أُغلق التقديم', UnderEvaluation: 'قيد التقييم',
          Clarification: 'استيضاح', Shortlisting: 'إعداد القائمة المختصرة', Recommendation: 'توصية',
          AwardApproval: 'اعتماد الترسية', Awarded: 'تمت الترسية', Completed: 'مكتمل', Cancelled: 'ملغى',
        },
        proposal: {
          Draft: 'مسودة', Submitted: 'مُقدَّم', UnderReview: 'قيد المراجعة',
          ClarificationRequested: 'مطلوب استيضاح', Revised: 'مُعدَّل', Shortlisted: 'ضمن القائمة المختصرة',
          NotSelected: 'غير مختار', AwardOffered: 'عرض ترسية', Awarded: 'تمت الترسية',
          Declined: 'مرفوض من المورد', Withdrawn: 'مسحوب',
          Lapsed: 'انتهت المهلة',            // [drafted] A-9/BRULE-052
          Cancelled: 'ملغى مع الطلب',        // [drafted] A-9/BRULE-056
        },
        evaluation: {
          NotStarted: 'لم يبدأ', Assigned: 'مُسند', InProgress: 'قيد التنفيذ',
          EvaluatorSubmitted: 'تم الإرسال', Consolidated: 'مُجمَّع', Finalized: 'نهائي',
        },
        award: {
          Recommended: 'موصى به', PendingApproval: 'بانتظار الاعتماد', Approved: 'معتمد',
          Rejected: 'مرفوض', Awarded: 'تمت الترسية',
        },
      },
      phone: {
        countryCode: 'رمز الدولة',
        localNumberPlaceholder: 'رقم الهاتف',
        other: 'أخرى',
        countries: {
          SY: 'سوريا (+963)',
          JO: 'الأردن (+962)',
          LB: 'لبنان (+961)',
          IQ: 'العراق (+964)',
          SA: 'السعودية (+966)',
          AE: 'الإمارات (+971)',
          QA: 'قطر (+974)',
          KW: 'الكويت (+965)',
          BH: 'البحرين (+973)',
          OM: 'عُمان (+968)',
          PS: 'فلسطين (+970)',
          TR: 'تركيا (+90)',
          EG: 'مصر (+20)',
        },
      },
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
        verifyFailed: 'تعذر تفعيل البريد الإلكتروني، الرابط غير صالح أو منتهي الصلاحية أو تم استخدامه من قبل',
        resendVerification: 'إعادة إرسال رابط التفعيل',
        resendSent: 'إذا كان الحساب موجوداً وغير مفعّل، تم إرسال رابط تفعيل جديد',
        mfaTitle: 'التحقق بخطوتين',
        mfaCodeLabel: 'رمز التطبيق المصادق (6 أرقام)',
        mfaSubmit: 'تأكيد',
        mfaInvalid: 'رمز غير صحيح، حاول مرة أخرى',
        mfaBack: 'رجوع',
      },
      dashboard: {
        welcome: 'مرحباً، {{email}}',
        supplierId: 'رقم المورد',
        permission: 'الصلاحية الحالية',
        placeholder: 'سيتم عرض ملخص الطلبات والعقود هنا لاحقاً.',
      },
      onboarding: {
        title: 'استكمال بيانات المورد',
        checklist: 'قائمة المتطلبات',
        // `onboarding.missing` is gone: the label now lives in status.document.Missing, so the
        // document chip has exactly one source like every other machine.
        submitBlockedTitle: 'لا يمكن إرسال الطلب بعد',
        submitBlockedIntro: 'الوثائق المطلوبة التالية ناقصة:',
        complete: 'مكتمل',
        save: 'حفظ',
        submit: 'إرسال الطلب',
        saved: 'تم الحفظ',
        saveFailed: 'تعذر الحفظ',
        submitted: 'تم إرسال الطلب للمراجعة',
        submitFailed: 'تعذر إرسال الطلب',
        incomplete: 'الملف غير مكتمل',
        readOnlyNotice: 'تم إرسال الطلب وهو الآن للقراءة فقط.',
        stepNavLabel: 'خطوات استكمال الملف',
        steps: { company: 'الشركة', contacts: 'جهات الاتصال', addresses: 'العناوين', banking: 'الحسابات المصرفية', offerings: 'الفئات المعروضة' },
        fields: {
          legalInfo: 'البيانات القانونية',
          legalNameAr: 'الاسم القانوني (عربي)',
          legalNameEn: 'الاسم القانوني (إنجليزي)',
          registrationNumber: 'رقم السجل التجاري',
          taxId: 'الرقم الضريبي',
          supplierType: 'نوع الكيان',
          establishedOn: 'تاريخ التأسيس',
          description: 'الوصف',
          website: 'الموقع الإلكتروني',
          supplierGroup: 'المجموعة',
          currencyCode: 'العملة',
          address: 'عنوان المقر الرئيسي',
          categoryLink: 'الفئات المعروضة',
          primaryContactPhone: 'هاتف جهة الاتصال الرئيسية',
          contact: 'جهة الاتصال',
          representative: 'الممثل',
          branch: 'الفرع',
          bankAccount: 'الحساب المصرفي',
          logo: 'شعار الشركة',
        },
        supplierTypes: { Company: 'شركة', Individual: 'فرد', Partnership: 'شراكة' },
        errors: { legalNameArRequired: 'الاسم القانوني (عربي) مطلوب', legalNameEnRequired: 'الاسم القانوني (إنجليزي) مطلوب' },
        logoTitle: 'شعار الشركة',
        logoAlt: 'شعار الشركة',
        noLogo: 'لا يوجد شعار',
        logoUpload: 'رفع شعار',
        logoReplace: 'استبدال الشعار',
        logoUploaded: 'تم رفع الشعار',
        logoUploadFailed: 'تعذر رفع الشعار',
        legalTitle: 'البيانات القانونية',
        profileTitle: 'بيانات الملف',
        documents: 'المستندات',
        requiredDocuments: 'مستندات مطلوبة',
        optionalDocuments: 'مستندات اختيارية',
        noOptionalDocuments: 'لا توجد مستندات اختيارية.',
        optional: 'اختياري',
        upload: 'رفع',
        reupload: 'إعادة الرفع',
        download: 'تنزيل',
        documentUploaded: 'تم رفع المستند',
        documentUploadFailed: 'تعذر رفع المستند',
        documentExpiryRequired: 'أدخل تاريخ الانتهاء قبل رفع هذا المستند',
        documentExpiryLabel: 'تاريخ الانتهاء',
        flagged: 'مطلوب تحديث',
        infoRequestedTitle: 'مطلوب معلومات إضافية',
        resubmit: 'إعادة الإرسال',
        resubmitted: 'تم إعادة إرسال الطلب للمراجعة',
        resubmitFailed: 'تعذر إعادة إرسال الطلب',
        termsLabel: 'الموافقة على الشروط والأحكام',
        termsTitle: 'الشروط والأحكام',
        termsCheckboxLabel: 'أقر بأنني قرأت ووافقت على الشروط والأحكام وسياسة معالجة البيانات الخاصة ببوابة الموردين.',
        termsAccept: 'موافقة',
        termsAccepted: 'تم تسجيل الموافقة على الشروط والأحكام',
        termsAcceptFailed: 'تعذر تسجيل الموافقة',
        termsAcceptedNotice: 'تمت الموافقة على الإصدار {{version}} من الشروط والأحكام بتاريخ {{date}}.',
        conflictTitle: 'لم يتم الحفظ — تم تعديل البيانات من مستخدم آخر',
        conflictBody: 'قام مستخدم آخر بحفظ تغييرات على هذا الملف أثناء تحريرك له. أعد تحميل الصفحة للاطلاع على أحدث البيانات ثم أعد إدخال تعديلاتك.',
        notFlaggedTitle: 'هذا الحقل غير مشمول بطلب المعلومات',
        notFlaggedBody: 'طلب منك المراجع تعديل أقسام محددة فقط. لا يمكن تعديل هذا الحقل حتى تكتمل المراجعة.',
      },
      contacts: {
        title: 'جهات الاتصال والممثلون',
        subtitle: 'الممثل الأساسي مخوّل بالتصرف نيابة عن المورد؛ جهات الاتصال الإضافية هي لأغراض التواصل العامة فقط.',
        representativesTitle: 'الممثلون',
        contactsTitle: 'جهات الاتصال الإضافية',
        addRepresentative: 'إضافة ممثل',
        editRepresentative: 'تعديل الممثل',
        addContact: 'إضافة جهة اتصال',
        editContact: 'تعديل جهة الاتصال',
        primary: 'أساسي',
        makePrimary: 'تعيين كأساسي',
        status: 'الحالة',
        actions: 'إجراءات',
        edit: 'تعديل',
        remove: 'حذف',
        save: 'حفظ',
        cancel: 'إلغاء',
        empty: 'لا توجد بيانات بعد.',
        fields: { fullName: 'الاسم الكامل', email: 'البريد الإلكتروني', phone: 'الهاتف', position: 'المنصب', role: 'الصفة' },
        errors: { fullNameRequired: 'الاسم الكامل مطلوب', emailInvalid: 'البريد الإلكتروني غير صحيح', removeFailed: 'تعذر الحذف', setPrimaryFailed: 'تعذر تعيين الممثل الأساسي' },
      },
      addresses: {
        title: 'العناوين والفروع',
        subtitle: 'يجب أن يكون لديك عنوان واحد على الأقل من نوع "المقر الرئيسي" لإرسال الطلب.',
        addressesTitle: 'العناوين',
        branchesTitle: 'الفروع',
        addAddress: 'إضافة عنوان',
        editAddress: 'تعديل العنوان',
        addBranch: 'إضافة فرع',
        editBranch: 'تعديل الفرع',
        actions: 'إجراءات',
        edit: 'تعديل',
        remove: 'حذف',
        save: 'حفظ',
        cancel: 'إلغاء',
        empty: 'لا توجد عناوين بعد.',
        emptyBranches: 'لا توجد فروع بعد.',
        missingHeadOffice: 'مطلوب عنوان واحد على الأقل من نوع "المقر الرئيسي" لإرسال الطلب — عنوان من نوع فرع أو فوترة فقط لا يكفي.',
        needHeadOfficeHint: 'لا يوجد لديك حالياً عنوان من نوع "المقر الرئيسي".',
        linkedAddressHint: 'اختياري — اربط هذا الفرع بأحد عناوينك.',
        kinds: { HeadOffice: 'المقر الرئيسي', Billing: 'الفوترة', Branch: 'فرع' },
        fields: {
          kind: 'النوع',
          line1: 'العنوان',
          line2: 'العنوان (سطر إضافي)',
          city: 'المدينة',
          regionCode: 'المنطقة',
          country: 'الدولة',
          postalCode: 'الرمز البريدي',
          nameAr: 'اسم الفرع (عربي)',
          nameEn: 'اسم الفرع (إنجليزي)',
          linkedAddress: 'العنوان المرتبط',
        },
        errors: {
          line1Required: 'العنوان مطلوب',
          cityRequired: 'المدينة مطلوبة',
          countryRequired: 'الدولة مطلوبة',
          nameArRequired: 'اسم الفرع (عربي) مطلوب',
          nameEnRequired: 'اسم الفرع (إنجليزي) مطلوب',
          removeFailed: 'تعذر الحذف',
        },
      },
      banking: {
        title: 'الحسابات المصرفية',
        subtitle: 'رقم الحساب مخفي دائماً ولا يظهر إلا بعد طلب صريح، ويُسجَّل كل عرض في سجل التدقيق.',
        accountsTitle: 'الحسابات المصرفية',
        addAccount: 'إضافة حساب',
        editAccount: 'تعديل الحساب',
        actions: 'إجراءات',
        edit: 'تعديل',
        remove: 'حذف',
        save: 'حفظ',
        cancel: 'إلغاء',
        empty: 'لا توجد حسابات مصرفية بعد.',
        reveal: 'إظهار',
        hide: 'إخفاء',
        revealFailed: 'تعذر إظهار رقم الحساب',
        default: 'الحساب الافتراضي',
        isDefault: 'افتراضي',
        makeDefault: 'تعيين كافتراضي',
        accountNumberEditHint: 'اتركه فارغاً للإبقاء على رقم الحساب الحالي دون تغيير.',
        fields: {
          accountHolderName: 'اسم صاحب الحساب',
          bankName: 'اسم البنك',
          branchName: 'اسم الفرع',
          accountNumber: 'رقم الحساب',
          swiftBic: 'رمز SWIFT/BIC',
          currencyCode: 'العملة',
        },
        errors: { accountHolderRequired: 'اسم صاحب الحساب مطلوب', bankNameRequired: 'اسم البنك مطلوب', accountNumberRequired: 'رقم الحساب مطلوب', removeFailed: 'تعذر الحذف' },
      },
      offerings: {
        title: 'الفئات المعروضة',
        subtitle: 'اختر الفئات التي يقدمها موردكم. مطلوبة فئة واحدة على الأقل لإرسال الطلب.',
        categoriesTitle: 'الفئات',
        empty: 'لا توجد فئات متاحة حالياً.',
        missingCategory: 'مطلوب اختيار فئة واحدة على الأقل لإرسال الطلب.',
      },
      offeringCatalog: {
        title: 'الخدمات المعروضة',
        subtitle: 'أنشئ وحرّر وعطّل ما يقدمه موردكم للجهات المشترية.',
        listTitle: 'قائمة الخدمات',
        empty: 'لا توجد خدمات معروضة بعد',
        add: 'إضافة خدمة',
        createTitle: 'إضافة خدمة معروضة',
        editTitle: 'تعديل الخدمة المعروضة',
        save: 'حفظ',
        cancel: 'إلغاء',
        edit: 'تعديل',
        deactivate: 'إلغاء التفعيل',
        status: 'الحالة',
        actions: 'إجراءات',
        active: 'نشطة',
        inactive: 'غير نشطة',
        created: 'تم إنشاء الخدمة',
        updated: 'تم تحديث الخدمة',
        deactivated: 'تم إلغاء تفعيل الخدمة',
        fields: {
          name: 'الاسم',
          nameAr: 'الاسم (عربي)',
          nameEn: 'الاسم (إنجليزي)',
          description: 'الوصف',
          category: 'الفئة',
          unit: 'وحدة القياس',
          price: 'السعر الاسترشادي',
          currency: 'العملة',
          attributes: 'خصائص إضافية',
          attributeKey: 'الخاصية',
          attributeValue: 'القيمة',
          addAttribute: 'إضافة خاصية',
          removeAttribute: 'إزالة',
        },
        errors: {
          required: 'هذا الحقل مطلوب',
          invalidCategory: 'فئة غير معروفة',
          invalidUnit: 'وحدة قياس غير معروفة',
          invalidCurrency: 'عملة غير معروفة',
          saveFailed: 'تعذر حفظ الخدمة',
          deactivateFailed: 'تعذر إلغاء تفعيل الخدمة',
        },
      },
      offeringSearch: {
        title: 'البحث عن الخدمات المعروضة',
        subtitle: 'ابحث في خدمات الموردين النشطين لأغراض دعوات طلب العروض.',
        filterCategory: 'الفئة',
        filterAll: 'الكل',
        searchPlaceholder: 'ابحث بالاسم…',
        empty: 'لا توجد نتائج',
        supplier: 'المورد',
        fields: { name: 'الاسم', category: 'الفئة', unit: 'وحدة القياس', price: 'السعر', attributes: 'خصائص إضافية' },
      },
      evaluationTemplates: {
        title: 'قوالب التقييم',
        subtitle: 'أنشئ وأدر قوالب معايير التقييم الموزونة القابلة لإعادة الاستخدام.',
        add: 'قالب جديد',
        empty: 'لا توجد قوالب بعد',
        createTitle: 'إنشاء قالب تقييم',
        save: 'حفظ',
        cancel: 'إلغاء',
        created: 'تم إنشاء القالب',
        criterionAdded: 'تمت إضافة المعيار',
        activated: 'تم تفعيل القالب',
        archived: 'تمت أرشفة القالب',
        forked: 'تم إنشاء نسخة جديدة من القالب',
        referenced: 'مرتبط بطلب عرض أسعار',
        weightTotal: 'مجموع الأوزان: {{total}}',
        addCriterion: 'إضافة معيار',
        activate: 'تفعيل',
        archive: 'أرشفة',
        fork: 'إنشاء نسخة جديدة',
        fields: { name: 'الاسم', nameAr: 'الاسم (عربي)', nameEn: 'الاسم (إنجليزي)', dimension: 'البُعد', weight: 'الوزن', maxScore: 'الحد الأقصى للنقاط', scoringType: 'نوع التقييم' },
        errors: { saveFailed: 'تعذر حفظ القالب', activateFailed: 'تعذر تفعيل القالب' },
      },
      rfq: {
        title: 'طلبات عروض الأسعار',
        subtitle: 'أنشئ وأدر طلبات عروض الأسعار عبر دورة حياتها الكاملة.',
        add: 'طلب جديد',
        listTitle: 'قائمة الطلبات',
        empty: 'لا توجد طلبات بعد',
        loadMore: 'عرض المزيد',
        createTitle: 'إنشاء طلب عرض أسعار',
        save: 'حفظ',
        cancel: 'إلغاء',
        created: 'تم إنشاء الطلب',
        itemAdded: 'تمت إضافة البند',
        requirementAdded: 'تمت إضافة المتطلب',
        templateBound: 'تم ربط قالب التقييم',
        submitted: 'تم إرسال الطلب للمراجعة',
        returned: 'تمت إعادة الطلب للتعديل',
        approved: 'تمت الموافقة على الطلب',
        published: 'تم نشر الطلب',
        closed: 'تم إغلاق باب التقديم',
        cancelled: 'تم إلغاء الطلب',
        submitForReview: 'إرسال للمراجعة',
        approve: 'موافقة',
        publish: 'نشر',
        // ── T-018 · ARABIC FOR REVIEW ──────────────────────────────────────────────
        // §7 has no deadline-change strings. Drafted in §7's register - NOT YET APPROVED,
        // in ARABIC-REVIEW.md's pile.
        deadline: {
          title: 'موعد إغلاق التقديم',                  // [reused] §12.4's own field name in Arabic
          help: 'التمديد من صلاحية موظف المشتريات، وتقديم الموعد من صلاحية المدير. يُبلَّغ جميع المدعوين بأي تغيير.', // [§7-style drafted]
          newDeadline: 'الموعد الجديد',                 // [§7-style drafted]
          reason: 'سبب التغيير',                        // [drafted] A-6
          apply: 'تغيير الموعد',                        // [§7-style drafted]
          changed: 'تم تغيير موعد إغلاق التقديم',        // [§7-style drafted]
          failed: 'تعذر تغيير موعد إغلاق التقديم',       // [§7-style drafted]
        },
        closeSubmission: 'إغلاق باب التقديم',
        manualCloseReason: 'إغلاق يدوي من قبل موظف المشتريات',
        returnForEditsTitle: 'إعادة للتعديل',
        returnForEdits: 'إعادة للتعديل',
        cancelTitle: 'إلغاء الطلب',
        cancelRfq: 'إلغاء الطلب',
        addItem: 'إضافة بند',
        addRequirement: 'إضافة متطلب',
        bindTemplate: 'ربط القالب',
        remove: 'إزالة',
        actions: 'إجراءات',
        noItems: 'لا توجد بنود بعد',
        attachments: {
          title: 'مرفقات الطلب',                            // [§7-style drafted]
          none: 'لا توجد مرفقات',                           // [reused] §4's empty-state pattern
          add: 'إضافة مرفق',                                // [§7-style drafted]
          added: 'تمت إضافة المرفق',                        // [§7-style drafted]
          download: 'تنزيل',                                // [reused] §7's own term
        },
        noRequirements: 'لا توجد متطلبات بعد',
        noTemplateBound: 'لم يتم ربط أي قالب تقييم بعد',
        boundTemplate: 'القالب المرتبط: {{id}} (الإصدار {{version}})',
        yes: 'نعم',
        no: 'لا',
        pending: 'قيد الانتظار',
        fields: {
          reference: 'المرجع', title: 'العنوان', titleAr: 'العنوان (عربي)', titleEn: 'العنوان (إنجليزي)',
          state: 'الحالة', currency: 'العملة', submissionOpensAt: 'فتح باب التقديم', submissionClosesAt: 'إغلاق باب التقديم',
          items: 'البنود', requirements: 'المتطلبات', category: 'الفئة', unit: 'وحدة القياس', quantity: 'الكمية',
          text: 'النص', textAr: 'النص (عربي)', textEn: 'النص (إنجليزي)', mandatory: 'إلزامي',
          evaluationTemplate: 'قالب التقييم', approvals: 'الموافقات', step: 'الخطوة', decision: 'القرار',
          comments: 'ملاحظات', reason: 'السبب',
        },
        errors: { saveFailed: 'تعذر حفظ الطلب', transitionFailed: 'تعذر تنفيذ الإجراء' },
        invitations: {
          title: 'الدعوات',
          none: 'لم تتم دعوة أي مورد بعد',
          candidatesTitle: 'موردون مقترحون',
          invite: 'دعوة',
          invited: 'تمت دعوة المورد',
          matchCount: '{{count}} تطابق فئة',
          fields: { supplier: 'المورد', status: 'الحالة', invitedAt: 'تاريخ الدعوة', viewedAt: 'تاريخ الاطلاع', declineReason: 'سبب الرفض' },
          errors: { inviteFailed: 'تعذرت دعوة المورد' },
        },
        clarifications: {
          title: 'الاستيضاحات',
          none: 'لا توجد أسئلة بعد',
          answer: 'إجابة',
          answerLabel: 'الإجابة',
          answered: 'تم حفظ الإجابة',
          publish: 'نشر للجميع',
          published: 'منشور للجميع',
          private: 'خاص بالسائل',
          // A-4: kept for the legacy-row publish button; the answer form no longer offers a choice.
          publishNow: 'نشر مباشرة',
          broadcastNotice: 'يُرسل الجواب إلى جميع المدعوين دون ذكر السائل.',   // [drafted]
          errors: { answerFailed: 'تعذر حفظ الإجابة' },
        },
        addenda: {
          title: 'الملاحق',
          none: 'لا توجد ملاحق بعد',
          issue: 'إصدار ملحق',
          issued: 'تم إصدار الملحق',
          descriptionAr: 'الوصف (عربي)',
          descriptionEn: 'الوصف (إنجليزي)',
          errors: { issueFailed: 'تعذر إصدار الملحق' },
        },
      },
      evaluation: {
        title: 'التقييم',
        open: 'فتح التقييم',
        opened: 'تم فتح التقييم',
        notOpened: 'لم يُفتح التقييم بعد',
        criteria: 'المعايير',
        dimension: 'البُعد',
        weight: 'الوزن',
        threshold: 'الحد الأدنى',
        envelope: 'المغلف',
        technicalEnvelope: 'فني',
        financialEnvelope: 'مالي',
        assignments: 'المقيّمون المعيّنون',
        evaluator: 'المقيّم',
        evaluatorUserId: 'معرّف المقيّم',
        assign: 'تعيين',
        assigned: 'تم تعيين المقيّم',
        noAssignments: 'لم يُعيَّن أي مقيّم بعد',
        submittedAt: 'تاريخ التقديم',
        recusedAt: 'الاستبعاد',
        recusedWithReason: 'مُستبعد: {{reason}}',
        recuse: 'استبعاد',
        recuseReason: 'سبب الاستبعاد',
        confirmRecuse: 'تأكيد الاستبعاد',
        recused: 'تم استبعاد المقيّم',
        results: 'النتائج',
        rank: 'الترتيب',
        proposal: 'العرض',
        qualified: 'التأهيل الفني',
        qualifiedYes: 'مؤهَّل',
        qualifiedNo: 'غير مؤهَّل',
        technicalScore: 'النتيجة الفنية',
        financialScore: 'النتيجة المالية',
        total: 'المجموع',
        consolidate: 'توحيد النتائج',
        consolidated: 'تم توحيد النتائج',
        finalize: 'اعتماد نهائي',
        finalized: 'تم الاعتماد النهائي',
        reopen: 'إعادة فتح',
        reopenReason: 'سبب إعادة الفتح',
        reopened: 'تمت إعادة فتح التقييم',
        errors: { actionFailed: 'تعذر تنفيذ الإجراء' },
        my: {
          title: 'تقييمي',
          notAssigned: 'أنت غير معيّن لهذا التقييم',
          proposal: 'العرض',
          score: 'الدرجة',
          scorePlaceholder: 'أدخل الدرجة',
          commentAr: 'ملاحظة (عربي)',
          commentEn: 'ملاحظة (إنجليزي)',
          save: 'حفظ الدرجة',
          saved: 'تم حفظ الدرجة',
          submit: 'تقديم التقييم',
          submitted: 'تم تقديم التقييم',
          alreadySubmitted: 'لقد قدّمت تقييمك بالفعل',
          financialLocked: 'المغلف المالي مقفل حتى يجتاز هذا العرض التأهيل الفني',
          qualified: 'مؤهَّل فنياً',
          notQualified: 'غير مؤهَّل فنياً',
          anonymousBidder: 'هوية المورد محجوبة أثناء التقييم',   // [drafted] A-8
          declaration: {
            title: 'إقرار تعارض المصالح',                        // [drafted] A-8/BRULE-067
            body: 'هذه أسماء الموردين المشاركين. إن كان لديك تعارض مصالح مع أيٍّ منهم فبيّنه الآن؛ بعد الإقرار تُحجب الأسماء ويجري التقييم دون معرفتها.', // [drafted]
            noConflict: 'لا يوجد تعارض — متابعة',                // [drafted]
            hasConflict: 'لديّ تعارض — تنحّي',                   // [drafted]
            reasonLabel: 'سبب التنحّي',                          // [drafted]
            reasonPlaceholder: 'سبب التنحّي',                    // [drafted]
            failed: 'تعذّر تسجيل الإقرار',                       // [drafted]
          },
          // ── T-067 · ARABIC FOR REVIEW ──────────────────────────────────────────────────
          // §7 has no table for an evaluator's workspace. Drafted here in §7's register:
          // professional MSA, authored rather than translated. NOT YET APPROVED - added to
          // ARABIC-REVIEW.md's pile alongside the four sets already waiting.
          //   [§7-style drafted]  authored here
          //   [reused]            already approved elsewhere in this file
          specification: 'المواصفات المطلوبة',      // [§7-style drafted]
          items: 'البنود',                          // [reused] §7's own column word for RfqItem
          requirements: 'المتطلبات',                // [reused] the RFQ authoring screen's own label
          mandatory: 'إلزامي',                      // [§7-style drafted]
          narrative: 'الشرح الفني',                 // [§7-style drafted] "technical narrative"
          answers: 'الردود على المتطلبات',           // [§7-style drafted]
          documents: 'المستندات الفنية',             // [§7-style drafted] Technical envelope only (D-7)
          errors: {
            scoreFailed: 'تعذر حفظ الدرجة',
            submitFailed: 'تعذر تقديم التقييم',
            documentFailed: 'تعذر فتح الملف {{fileName}}', // [§7-style drafted]
          },
        },
      },
      comparison: {
        // A-1/BRULE-069's surfaced tie. Drafted, marked, in ARABIC-REVIEW.md's pile.
        // B-1/SCR-433: the clarification request, reachable for the first time.
        clarifyTitle: 'طلب استيضاح من مورد',                     // [drafted]
        clarifyBody: 'اطلب من المورد توضيحاً حول عرضه. يُبلَّغ المورد ويعود العرض إلى حالة «مطلوب استيضاح».', // [drafted]
        clarifyReason: 'سبب الاستيضاح لـ {{code}}',              // [drafted]
        clarifyReasonPlaceholder: 'ما المطلوب توضيحه',            // [drafted]
        clarifyAsk: 'طلب استيضاح',                               // [reused] §8's «استيضاح»
        clarifyRequested: 'تم إرسال طلب الاستيضاح',               // [drafted]
        clarifyFailed: 'تعذّر إرسال طلب الاستيضاح',               // [drafted]
        tieUnresolved: 'تعادل غير محلول',                        // [drafted]
        tieTitle: 'تعادل في الترتيب يحتاج قراراً',                 // [drafted]
        tieBody: 'تساوت العروض التالية في كل معايير الترجيح. اختر العرض الأول مع بيان السبب؛ لا يمكن الترسية قبل ذلك.', // [drafted]
        tieReason: 'سبب اختيار {{code}}',                        // [drafted]
        tieReasonPlaceholder: 'سبب القرار',                       // [drafted]
        tieResolve: 'تثبيت الترتيب',                              // [drafted]
        tieResolved: 'تم تثبيت الترتيب',                          // [drafted]
        tieResolveFailed: 'تعذّر تثبيت الترتيب',                  // [drafted]
        title: 'مقارنة العروض',
        notFound: 'المقارنة غير متاحة',
        empty: 'لا توجد عروض مقدَّمة بعد',
        proposalCount: '{{count}} عرض مقدَّم',
        proposalCount_other: '{{count}} عروض مقدَّمة',
        awaitingConsolidation: 'بانتظار توحيد نتائج التقييم',
        rowLabel: 'البند',
        notVisible: 'غير متاح',
        groups: { commercial: 'تجاري', requirements: 'المتطلبات', evaluation: 'التقييم' },
        grandTotal: 'الإجمالي الكلي',
        paymentTerms: 'شروط الدفع',
        incoterm: 'شروط التسليم الدولية',
        validityEnd: 'تاريخ انتهاء الصلاحية',
        met: 'مستوفى',
        notMet: 'غير مستوفى',
        pass: 'ناجح',
        fail: 'راسب',
        qualification: 'التأهيل الفني',
        qualified: 'مؤهَّل',
        notQualified: 'غير مؤهَّل',
        weightedTotal: 'المجموع المرجَّح',
        rank: 'الترتيب',
      },
      workspace: {
        title: 'سير عمل الطلب',
        stages: 'مراحل دورة الحياة',
        noNextAction: 'لا توجد خطوة تالية متاحة حالياً.',
        cancelledBanner: 'تم إلغاء هذا الطلب.',
      },
      award: {
        title: 'الترسية',
        status: 'حالة الترسية',
        notRecommendedYet: 'لم يتم ترشيح فائز بعد',
        recommend: 'ترشيح الفائز',
        reRecommend: 'إعادة الترشيح',
        selectWinner: 'اختر العرض الفائز',
        winnerOption: 'الترتيب {{rank}} — المجموع المرجَّح {{total}}',
        noQualifiedProposals: 'لا توجد عروض مؤهَّلة فنياً بعد',
        justification: 'المبرر',
        justificationAr: 'المبرر (عربي)',
        justificationEn: 'المبرر (إنجليزي)',
        revision: 'المراجعة رقم {{count}}',
        recommended: 'تم تسجيل الترشيح',
        routeForApproval: 'إرسال للاعتماد',
        routed: 'تم إرسال الترشيح للاعتماد',
        approvals: 'خطوات الاعتماد',
        stepLabel: 'الخطوة {{step}}',
        pending: 'قيد الانتظار',
        approve: 'اعتماد',
        approved: 'تم اعتماد الترسية',
        reject: 'رفض',
        rejectReason: 'سبب الرفض',
        rejected: 'تم رفض الترشيح',
        rejectionReason: 'سبب الرفض',
        execute: 'إصدار الترسية',
        issued: 'تم إصدار الترسية',
        erpStatus: 'حالة مزامنة نظام تخطيط الموارد',
        externalPoRef: 'مرجع أمر الشراء',
        retrySync: 'إعادة محاولة المزامنة',
        retryQueued: 'تم إعادة جدولة المزامنة',
        errors: { actionFailed: 'تعذر تنفيذ الإجراء', segregationOfDuties: 'يجب أن يختلف المعتمد عن مرشّح الفائز' },
      },
      supplierRfq: {
        deadlineChanged: {
          title: 'تغيّر موعد إغلاق التقديم',                  // [drafted] A-6
        },
        attachments: {
          title: 'مرفقات الطلب',                            // [reused] matches rfq.attachments
          none: 'لا توجد مرفقات',                           // [reused]
          download: 'تنزيل',                                // [reused] §7's own term
          downloadFailed: 'تعذّر تنزيل المرفق',              // [§7-style drafted]
        },
        title: 'طلبات عروض الأسعار',
        subtitle: 'طلبات العروض التي دُعيت للمشاركة فيها.',
        listTitle: 'قائمة الدعوات',
        empty: 'لا توجد دعوات بعد',
        loadMore: 'عرض المزيد',
        myStatus: 'حالتي',
        notFound: 'الطلب غير موجود',
        declineTitle: 'رفض الدعوة',
        decline: 'رفض الدعوة',
        declineReasonPlaceholder: 'سبب الرفض (اختياري)',
        declined: 'تم رفض الدعوة',
        errors: { declineFailed: 'تعذر رفض الدعوة' },
        clarifications: {
          title: 'الاستيضاحات',
          none: 'لا توجد أسئلة بعد',
          mine: 'سؤالي',
          awaitingAnswer: 'بانتظار الإجابة',
          askPlaceholder: 'اكتب سؤالك…',
          ask: 'إرسال السؤال',
          asked: 'تم إرسال السؤال',
          errors: { askFailed: 'تعذر إرسال السؤال' },
        },
      },
      proposal: {
        title: 'العرض',
        // SCR-151: "*Concurrency conflict:* Dialog 'This proposal changed in another tab/user' →
        // reload/merge". Reload only - there is no merge UI, and inventing one was not specified.
        conflictTitle: 'تم تعديل هذا العرض في مكان آخر',
        conflictBody: 'قام مستخدم آخر - أو تبويب آخر - بتعديل هذا العرض بعد فتحك له. أعد التحميل للاطلاع على النسخة الحالية قبل حفظ تغييراتك.',
        conflictReload: 'إعادة التحميل',
        start: 'بدء تقديم العرض',
        goToMyProposal: 'الذهاب إلى عرضي',
        submit: 'إرسال العرض',
        submitted: 'تم إرسال العرض',
        pricing: 'التسعير',
        unitPrice: 'سعر الوحدة',
        lineTotal: 'إجمالي البند',
        savePrice: 'حفظ السعر',
        itemPriced: 'تم حفظ سعر البند',
        requirements: 'المتطلبات',
        noRequirements: 'لا توجد متطلبات',
        saveAnswer: 'حفظ الإجابة',
        requirementAnswered: 'تم حفظ الإجابة',
        terms: 'الشروط التجارية',
        currency: 'العملة',
        paymentTerms: 'شروط الدفع',
        incoterm: 'شروط التسليم (Incoterm)',
        validityEnd: 'تاريخ نهاية الصلاحية',
        saveTerms: 'حفظ الشروط',
        termsSaved: 'تم حفظ الشروط',
        documents: 'المستندات',
        noDocuments: 'لا توجد مستندات بعد',
        envelope: 'المغلف',                                  // [drafted] A-2
        envelopeCommercial: 'المغلف المالي',                  // [reused] §7's own term
        envelopeTechnical: 'المغلف الفني',                    // [reused] §7's own term
        envelopeExpected: {
          Technical: 'يُتوقع أن يكون هذا المستند في المغلف الفني.',   // [drafted] A-2
          Commercial: 'يُتوقع أن يكون هذا المستند في المغلف المالي.', // [drafted] A-2
        },
        uploadDocument: 'رفع مستند',
        documentAdded: 'تمت إضافة المستند',
        withdrawTitle: 'سحب العرض',
        withdraw: 'سحب العرض',
        withdrawReasonPlaceholder: 'سبب السحب',
        withdrawn: 'تم سحب العرض',
        // ── T-064 · ARABIC FOR REVIEW ──────────────────────────────────────────────
        // §7 has no award-offer strings. Drafted in §7's register - NOT YET APPROVED, in
        // ARABIC-REVIEW.md's pile. «اعتذار» not «رفض» for declining, matching the invitation
        // register and the proposal.declined notification.
        awardOfferedTitle: 'عرض ترسية',                    // [§7-style drafted]
        awardOfferedBody: 'اختير عرضكم للترسية. يمكنكم الاعتذار عن الترسية مع بيان السبب، أو انتظار تأكيد الجهة.', // [§7-style drafted]
        decline: 'الاعتذار عن الترسية',                     // [§7-style drafted]
        declineReason: 'سبب الاعتذار',                      // [§7-style drafted]
        declineReasonPlaceholder: 'سبب الاعتذار عن الترسية', // [§7-style drafted]
        declined: 'تم تسجيل اعتذاركم',                      // [§7-style drafted]
        errors: {
          startFailed: 'تعذر بدء العرض', saveFailed: 'تعذر الحفظ',
          submitFailed: 'تعذر إرسال العرض', withdrawFailed: 'تعذر سحب العرض',
          declineFailed: 'تعذر تسجيل الاعتذار',             // [§7-style drafted]
        },
      },
      team: {
        title: 'إدارة الفريق',
        subtitle: 'ادعُ مستخدمين إضافيين للوصول إلى ملف موردكم وإدارته.',
        membersTitle: 'أعضاء الفريق',
        loadMore: 'عرض المزيد',
        invite: 'دعوة عضو',
        inviteTitle: 'دعوة عضو جديد',
        inviteDescription: 'سيتلقى العضو المدعو رابطاً بالبريد الإلكتروني لتعيين كلمة المرور.',
        sendInvite: 'إرسال الدعوة',
        cancel: 'إلغاء',
        inviteSent: 'تم إرسال الدعوة',
        inviteFailed: 'تعذر إرسال الدعوة',
        duplicateEmail: 'يوجد حساب مسجل بهذا البريد الإلكتروني بالفعل',
        disable: 'تعطيل',
        disabled: 'تم تعطيل العضو',
        disableFailed: 'تعذر تعطيل العضو',
        empty: 'لا يوجد أعضاء بعد.',
        status: 'الحالة',
        actions: 'إجراءات',
        active: 'نشط',
        disabledStatus: 'معطّل',
        fields: { fullName: 'الاسم الكامل', email: 'البريد الإلكتروني' },
        errors: { fullNameRequired: 'الاسم الكامل مطلوب', emailInvalid: 'البريد الإلكتروني غير صحيح', passwordTooShort: 'كلمة المرور قصيرة جداً' },
        acceptInviteTitle: 'قبول الدعوة',
        acceptInviteHint: 'عيّن كلمة مرور لإكمال الانضمام إلى فريق المورد.',
        acceptInviteSubmit: 'قبول الدعوة',
        acceptInviteSuccess: 'تم قبول الدعوة بنجاح، يمكنك الآن تسجيل الدخول',
        acceptInviteInvalid: 'الرابط غير صالح أو منتهي الصلاحية',
      },
      review: {
        title: 'مراجعة طلبات الموردين',
        queue: 'قائمة المراجعة',
        age: 'المدة',
        reviewTarget: 'الموعد المستهدف',                 // [drafted] A-5
        assignee: 'المسؤول',
        actions: 'إجراءات',
        filterState: 'الحالة',
        filterAssignee: 'المسؤول',
        filterAll: 'الكل',
        claim: 'تولي المراجعة',
        claimed: 'تم تولي المراجعة',
        claimFailed: 'تعذر تولي المراجعة',
        unassign: 'إلغاء التعيين',
        unassigned: 'تم إلغاء التعيين',
        unassignFailed: 'تعذر إلغاء التعيين',
        unassignedLabel: 'غير معيّن',
        assignedToMe: 'معيّن لك',
        assignedToOther: 'معيّن لمراجع آخر',
        noItems: 'لا توجد طلبات بانتظار المراجعة',
        loadMore: 'عرض المزيد',
        backToQueue: 'العودة للقائمة',
        pickUp: 'بدء المراجعة',
        pickUpFailed: 'تعذر بدء المراجعة',
        approve: 'اعتماد',
        approveFailed: 'تعذر الاعتماد',
        reject: 'رفض',
        rejectFailed: 'تعذر الرفض',
        suspend: 'تعليق',
        reactivate: 'إعادة التفعيل',
        deactivate: 'إلغاء التفعيل',
        deactivateWarning: 'إلغاء التفعيل نهائي ولا يمكن التراجع عنه. سيتم إلغاء وصول جميع مستخدمي المورد فوراً.',
        lifecycleFailed: 'تعذر تغيير حالة المورد',
        requestInfo: 'طلب معلومات',
        requestInfoFailed: 'تعذر إرسال طلب المعلومات',
        reason: 'السبب',
        flagProfileFields: 'الحقول المطلوب تعديلها',
        flagDocuments: 'المستندات المطلوب تعديلها',
        submit: 'إرسال',
        cancel: 'إلغاء',
        profile: 'الملف',
        legalInfo: 'البيانات القانونية',
        addresses: 'العناوين',
        representatives: 'الممثلون',
        contacts: 'جهات الاتصال الإضافية',
        documents: 'المستندات',
        annotationHistory: 'سجل طلبات المعلومات',
        decisionSuccess: 'تم تسجيل القرار',
      },
      staff: {
        title: 'الموظفون',
        subtitle: 'دعوة موظفين جدد للإدارة الداخلية وتعيين أدوارهم.',
        invite: 'دعوة',
        inviteTitle: 'دعوة موظف',
        hint: 'دعوة موظف جديد بالبريد الإلكتروني والدور. سيحصل على رابط لتعيين كلمة المرور الخاصة به.',
        cancel: 'إلغاء',
        invited: 'تم إرسال الدعوة إلى {{email}}',
        fields: { fullName: 'الاسم الكامل', email: 'البريد الإلكتروني', role: 'الدور' },
        roles: {
          onboarding_reviewer: 'مراجع استكمال الموردين',
          procurement_officer: 'مسؤول المشتريات',
          procurement_manager: 'مدير المشتريات',
          evaluator: 'مقيّم',
          ministry_viewer: 'مشاهد الوزارة',
          system_admin: 'مسؤول النظام',
        },
        errors: {
          fullNameRequired: 'الاسم الكامل مطلوب',
          emailInvalid: 'البريد الإلكتروني غير صحيح',
          inviteFailed: 'تعذر إرسال الدعوة',
          loadFailed: 'تعذّر تحميل حسابات الموظفين',              // [drafted]
          updateFailed: 'تعذّر تنفيذ الإجراء',                    // [drafted]
          cannotActOnSelf: 'لا يمكنك تنفيذ هذا الإجراء على حسابك.', // [drafted]
          wouldLockOutAdministration: 'لا يمكن تعطيل آخر مسؤول نظام مفعّل.', // [drafted]
          passwordTooShort: 'كلمة المرور قصيرة جداً',
        },
        // T-077/SCR-701/702. Drafted in §7's register, marked, in ARABIC-REVIEW.md.
        accountsTitle: 'حسابات الموظفين',                     // [drafted]
        noAccounts: 'لا توجد حسابات',                          // [reused] §4's empty-state pattern
        status: 'الحالة',                                      // [reused] §7's own term
        actions: 'إجراءات',                                    // [reused]
        active: 'مفعّل',                                        // [reused]
        inactive: 'معطّل',                                      // [drafted]
        mfaOn: 'التحقق بخطوتين مُفعّل',                          // [drafted]
        sessions: 'جلسات نشطة: {{count}}',                      // [drafted]
        deactivate: 'تعطيل',                                    // [drafted]
        reactivate: 'إعادة التفعيل',                            // [drafted]
        resetMfa: 'إعادة ضبط التحقق بخطوتين',                   // [drafted]
        roleChanged: 'تم تغيير الدور',                          // [drafted]
        mfaReset: 'تمت إعادة ضبط التحقق بخطوتين',                // [drafted]
        retry: 'إعادة المحاولة',                                // [reused]
        acceptInviteTitle: 'قبول دعوة الموظف',
        acceptInviteHint: 'عيّن كلمة مرور لإكمال إعداد حسابك.',
        acceptInviteSuccess: 'تم إعداد حسابك. يمكنك الآن تسجيل الدخول.',
        acceptInviteInvalid: 'رابط الدعوة غير صالح أو منتهي الصلاحية أو تم استخدامه بالفعل.',
        acceptInviteSubmit: 'تعيين كلمة المرور',
      },
      roleManagement: {
        title: 'الأدوار والصلاحيات',
        subtitle: 'تعديل مجموعة الصلاحيات لكل دور. يسري التغيير على تسجيل الدخول التالي لكل مستخدم يحمل هذا الدور.',
        errors: {
          invalidPermission: 'صلاحية غير معروفة',
          wouldLockOutRoleManagement: 'لا يمكن إزالة هذه الصلاحية - لن يتمكن أحد بعدها من تعديل الأدوار مطلقاً',
          updateFailed: 'تعذر حفظ التغيير',
        },
      },
      organizations: {
        title: 'الجهات',
        subtitle: 'إنشاء الجهات وإدارة روابطها مع الموردين (يدوياً فقط، بدون ربط تلقائي).',
        listTitle: 'الجهات',
        empty: 'لا توجد جهات بعد',
        createTitle: 'إنشاء جهة',
        save: 'حفظ',
        cancel: 'إلغاء',
        add: 'إضافة',
        remove: 'إزالة',
        lookup: 'بحث',
        manageOrgUnits: 'إدارة الوحدات',
        orgUnitsCount: 'عدد الوحدات',
        orgUnitsTitle: 'وحدات {{name}}',
        noOrgUnits: 'لا توجد وحدات بعد',
        actions: 'إجراءات',
        created: 'تم إنشاء الجهة',
        linksTitle: 'روابط المورد بالجهات',
        linkAdd: 'إضافة رابط',
        linkCreated: 'تم إنشاء الرابط',
        noLinks: 'لا توجد روابط لهذا المورد',
        types: { Hotel: 'فندق', MotBody: 'جهة تابعة لوزارة السياحة', Ministry: 'الوزارة' },
        fields: {
          legalNameAr: 'الاسم القانوني (عربي)',
          legalNameEn: 'الاسم القانوني (إنجليزي)',
          organizationType: 'نوع الجهة',
          contactEmail: 'البريد الإلكتروني',
          contactPhone: 'الهاتف',
          orgUnitName: 'اسم الوحدة',
          supplierReferenceCode: 'الرمز المرجعي للمورد',
          organization: 'الجهة',
        },
        errors: {
          nameRequired: 'الاسم مطلوب',
          createFailed: 'تعذر إنشاء الجهة',
          orgUnitFailed: 'تعذر تنفيذ العملية على الوحدة',
          linkFailed: 'تعذر تنفيذ العملية على الرابط',
        },
      },
      register: {
        title: 'تسجيل مورد جديد',
        displayNameAr: 'اسم الشركة (عربي)',
        displayNameEn: 'اسم الشركة (إنجليزي)',
        registrationNumber: 'رقم السجل التجاري',
        registrationNumberHint: 'اختياري عند التسجيل، مطلوب لاحقاً لإكمال الملف',
        representativeName: 'اسم الممثل الرئيسي',
        representativePhone: 'هاتف الممثل الرئيسي',
        confirmPassword: 'تأكيد كلمة المرور',
        submit: 'إنشاء حساب',
        haveAccount: 'لديك حساب بالفعل؟ تسجيل الدخول',
        createAccount: 'إنشاء حساب جديد',
        successTitle: 'تم إنشاء الحساب',
        closedTitle: 'التسجيل مغلق حالياً',                  // [§7-style drafted]
        closedBody: 'التسجيل الذاتي مغلق حالياً. يرجى التواصل مع الوزارة لإتمام التسجيل.', // [§7-style drafted]
        checkEmail: 'تحقق من بريدك الإلكتروني لتفعيل الحساب. رقم المرجع الخاص بك:',
        duplicateEmail: 'يوجد حساب مسجل بهذا البريد الإلكتروني بالفعل',
        weakPassword: 'كلمة المرور لا تفي بمتطلبات القوة',
        failed: 'تعذر إنشاء الحساب',
      },
      settings: {
        title: 'إعدادات الحساب',
        mfaTitle: 'المصادقة الثنائية',
        mfaEnroll: 'تفعيل المصادقة الثنائية',
        mfaScanOrEnter: 'امسح الرمز باستخدام تطبيق المصادقة أو أدخل المفتاح يدوياً',
        mfaCodeLabel: 'رمز التحقق',
        mfaConfirm: 'تأكيد',
        mfaEnabled: 'تم تفعيل المصادقة الثنائية',
        mfaEnrollFailed: 'تعذر بدء التفعيل',
        mfaInvalidCode: 'رمز غير صحيح',
        recoveryCodesNotice: 'احفظ رموز الاسترداد هذه في مكان آمن — لن تظهر مرة أخرى.',
        sessionsTitle: 'الجلسات النشطة',
        // B-1/FR-AUD-003: the supplier's own trail, reachable for the first time.
        auditTitle: 'سجل نشاط حسابي',                            // [drafted]
        auditHint: 'أحدث الأحداث المسجّلة على حسابك، من الأحدث إلى الأقدم.', // [drafted]
        auditEmpty: 'لا توجد أحداث مسجّلة بعد',                   // [reused] §4's empty-state pattern
        auditExport: 'تنزيل السجل (CSV)',                        // [drafted]
        auditExportFailed: 'تعذّر تنزيل السجل',                   // [drafted]
        auditLoadFailed: 'تعذّر تحميل السجل',                     // [drafted]
        retry: 'إعادة المحاولة',                                 // [reused]
        currentSession: 'الجلسة الحالية',
        unknownDevice: 'جهاز غير معروف',
        revoke: 'إنهاء الجلسة',
        loadMoreSessions: 'عرض المزيد',
        revokeAllOthers: 'تسجيل الخروج من جميع الأجهزة الأخرى',
        sessionRevoked: 'تم إنهاء الجلسة',
        sessionsRevokedAll: 'تم تسجيل الخروج من جميع الأجهزة الأخرى',
      },
      language: 'English',
    },
  },
  en: {
    translation: {
      appName: 'Supplier Portal',
      nav: { home: 'Home', dashboard: 'Dashboard', onboarding: 'Complete Profile', offerings: 'Offerings', team: 'Team', settings: 'Settings', backOffice: 'Back Office', logout: 'Log out', mobileTabBarLabel: 'Primary navigation', rfqs: 'RFQs' },
      supplierDashboard: {
        title: 'Supplier dashboard',
        greeting: 'Welcome, {{name}}',
        kpis: {
          openInvitations: 'Open invitations',
          draftProposals: 'Draft proposals',
          submittedProposals: 'Submitted proposals',
          documentsNeedingAttention: 'Documents needing attention',
        },
        actionRequired: {
          title: 'Needs your attention',
          expiringDocuments: 'Documents expiring ({{count}})',
          rejectedDocuments: 'Rejected documents ({{count}})',
          invitationsClosingSoon: 'Invitations closing soon ({{count}})',
          clarificationsAnswered: 'Clarifications answered ({{count}})',
          awardOffers: 'Award offers ({{count}})',
          dismiss: 'Dismiss',
        },
        invitations: 'Invitations & deadlines',
        proposals: 'My proposals',
        validUntil: 'Valid until {{date}}',
        noValidity: 'No validity date',
        profileHealth: 'Profile & document health',
        completeness: 'Required documents: {{done}} of {{total}}',
        nextDocument: 'Next required document: {{code}}',
        allDocuments: 'All required documents are in place.',
        notifications: 'Recent notifications',
        openNotifications: 'View notifications',
        pendingTitle: 'Your application is under review',
        pendingBody: "We'll let you know as soon as your profile is approved. You can keep completing it in the meantime.",
        pendingCta: 'Continue your profile',
        erpDegraded: 'Purchase-order sync is paused. This does not affect your proposal.',
        emptyTitle: 'No invitations yet',
        emptyBody: "RFQ invitations will appear here when a buyer invites you.",
        loadFailed: "Couldn't load this section",
        retry: 'Try again',
      },
      notificationTemplates: {
        title: 'Notification templates',
        subtitle: 'The wording of each notification, in Arabic and English. A type nobody has changed uses the shipped wording.',
        shipped: 'Shipped wording',
        overridden: 'Overridden',
        overriddenAt: 'Changed {{at}}',
        edit: 'Edit',
        collapse: 'Hide',
        titleAr: 'Title (Arabic)',
        titleEn: 'Title (English)',
        bodyAr: 'Body (Arabic)',
        bodyEn: 'Body (English)',
        tokens: 'Available tokens: {{tokens}}',
        noTokens: 'This type has no tokens.',
        shippedCopy: 'Shipped wording (what revert restores)',
        save: 'Save',
        saved: 'Wording saved',
        revert: 'Restore the shipped wording',
        reverted: 'Shipped wording restored',
        loadFailed: 'Could not load the templates',
        retry: 'Try again',
        errors: {
          unknownTokens: 'This notification cannot fill: {{tokens}}',
          saveFailed: 'Could not save the wording',
          revertFailed: 'Could not restore the shipped wording',
        },
      },
      systemSettings: {
        title: 'System settings',
        subtitle: 'Values that apply across the whole system. A setting nobody has changed runs on its default.',
        value: 'Value',
        save: 'Save',
        saved: 'Setting saved',
        overridden: 'Overridden',
        overriddenAt: 'Changed {{at}}',
        usingDefault: 'Using the default ({{value}})',
        loadFailed: 'Could not load the settings',
        retry: 'Try again',
        keys: {
          'registration.mode': 'Supplier registration',
          'proposals.defaultCurrencyCode': 'Default currency',
          'documents.expiringSoonWindowDays': 'Expiring-soon window (days)',
          'documents.renewalReminderDays': 'Renewal reminder days',
        },
        help: {
          'registration.mode': 'When closed, self-registration is refused and applicants are asked to contact the Ministry.',
          'proposals.defaultCurrencyCode': 'Pre-selected on proposal forms. Must be an active currency.',
          'documents.expiringSoonWindowDays': 'Decides when a document becomes Expiring soon. Independent of the reminder days.',
          'documents.renewalReminderDays': 'Days before expiry on which a reminder is sent. No repeats.',
        },
        choices: {
          'registration.mode': {
            open: 'Open for self-registration',
            closed: 'Closed',
          },
        },
        hints: {
          integerList: 'Comma-separated numbers, e.g. 30,14,3',
          range: 'Between {{min}} and {{max}}',
        },
        errors: {
          value_required: 'A value is required',
          value_not_allowed: 'That value is not allowed',
          value_out_of_range: 'That value is outside the allowed range',
          value_has_duplicates: 'The same number cannot appear twice',
          reference_code_not_active: 'That code does not exist or is not active',
          unknown: 'Could not save the setting',
        },
      },
      adminOverview: {
        title: 'Platform administration',
        kpis: {
          users: 'Users',
          roles: 'Roles',
          outboxPending: 'Outbox pending',
          auditRows: 'Audit records (24h)',
        },
        outbox: 'Outbox',
        outboxPending: 'Pending',
        outboxFailed: 'Failed',
        outboxOldest: 'Oldest pending message',
        outboxDrained: 'Nothing pending',
        outboxFailedWarning: 'Failed messages need attention',
        erpNotConfigured: 'No real ERP integration is configured',
        erpNotConfiguredBody: 'Messages are written to the log and sent nowhere. A message marked Sent is not evidence that data reached an external system.',
        minutes: '{{value}} min',
        jobs: 'Recurring jobs',
        jobsDisabled: 'Recurring jobs are disabled',
        jobsDisabledBody: 'Reminders will not be sent and RFQs will not close automatically until recurring jobs are enabled in the deployment configuration.',
        jobsMissing: 'Jobs missing from the schedule',
        jobsHealthy: '{{value}} jobs registered',
        referenceData: 'Reference data',
        referenceEmpty: 'A reference table has no active codes - registration will fail',
        activeOfTotal: '{{active}} of {{total}}',
        tables: {
          categories: 'Categories',
          'document-types': 'Document types',
          currencies: 'Currencies',
          'units-of-measure': 'Units of measure',
          regions: 'Regions',
        },
        loadFailed: 'Could not load platform administration',
        retry: 'Try again',
      },
      ministry: {
        title: 'Governance dashboard',
        kpis: {
          suppliers: 'Registered suppliers',
          rfqs: 'RFQs',
          awards: 'Awards',
          participation: 'Average proposals per RFQ',
        },
        awardedValue: 'Total awarded value',
        commercialWithheld: 'Commercial values are not shown',
        commercialWithheldBody: 'Under the current visibility policy, aggregate metrics are shown without commercial values.',
        suppliersByState: 'Suppliers by lifecycle state',
        rfqsByState: 'RFQs by state',
        empty: 'No data yet',
        loadFailed: 'Could not load the governance dashboard',
        retry: 'Try again',
      },
      reports: {
        title: 'Reports',
        from: 'From',
        to: 'To',
        state: 'State',
        count: 'Count',
        interval: 'Interval',
        sampleSize: 'RFQs measured',
        medianHours: 'Median hours',
        notMeasured: '(not measured)',
        noRows: 'No data',
        exportPdf: 'Export PDF',
        exportCsv: 'Export CSV',
        loadFailed: 'The report could not be loaded.',
        downloadFailed: 'The file could not be downloaded.',
        retry: 'Try again',
        intervals: {
          DraftToReview: 'Draft to review',
          ReviewToApproved: 'Review to approved',
          ApprovedToPublished: 'Approved to published',
          PublishedToSubmissionClosed: 'Published to submission closed',
          SubmissionClosedToEvaluation: 'Submission closed to evaluation',
          EvaluationToAward: 'Evaluation to award',
        },
        procurement: {
          title: 'Procurement report',
          rfqsByState: 'RFQs by state',
          cycleTime: 'Cycle time',
          awardsByState: 'Awards by state',
          coverageFloor: 'Cycle times are measured from {{date}} onward; earlier RFQs are not included.',
          coverageNone: 'No recorded transitions yet, so cycle time cannot be measured.',
        },
        compliance: {
          title: 'Compliance report',
          suppliersByState: 'Suppliers by lifecycle state',
          documentsByState: 'Documents by state (latest versions)',
          registryScope: 'These counts cover every registered supplier, not only your organization.',
        },
      },
      procurementDashboard: {
        title: 'Procurement dashboard',
        from: 'From', to: 'To',
        newRfq: 'New RFQ',
        kpis: {
          activeRfqs: 'Active RFQs',
          closingThisWeek: 'Closing this week',
          awaitingMyAction: 'Awaiting my action',
          pendingApprovals: 'Pending approvals',
          awardsInProgress: 'Awards in progress',
        },
        pipeline: 'Pipeline',
        tasks: 'Deadlines & tasks',
        taskKinds: {
          SubmissionClosing: 'Submission closing',
          EvaluationDue: 'Evaluation due',
          RecommendationPending: 'Recommendation pending',
        },
        noTasks: 'Nothing due',
        activity: 'Recent activity',
        openNotifications: 'View notifications',
        approvals: 'Approvals',
        openApprovals: 'Open approval queues',
        emptyTitle: 'No RFQs yet',
        emptyBody: 'Create your first RFQ to invite suppliers and collect proposals.',
        loadFailed: "Couldn't load the dashboard",
        retry: 'Try again',
      },
      approvals: {
        title: 'Approvals',
        subtitle: 'Work waiting for approval in your organization.',
        rfqQueue: 'RFQs awaiting publish approval',
        awardQueue: 'Awards awaiting approval',
        noRfqs: 'No RFQs are waiting for approval',
        noAwards: 'No awards are waiting for approval',
        loadFailed: "Couldn't load the approval queues",
        retry: 'Try again',
      },
      reviewDashboard: {
        title: 'Onboarding review',
        openQueue: 'Open the review queue',
        kpis: {
          pending: 'Pending',
          underReview: 'Under review',
          infoRequested: 'Info requested',
          unassigned: 'Unassigned',
          assignedToMe: 'Assigned to me',
        },
        aging: 'Waiting time',
        oldestCase: 'The oldest open case has been waiting {{days}} days.',
        noOpenCases: 'No open cases.',
        watchlist: 'Documents nearing expiry',
        noExpiring: 'No documents are nearing expiry',
        loadFailed: "Couldn't load the dashboard",
        retry: 'Try again',
      },
      evaluationDashboard: {
        title: 'My evaluations',
        tabs: { Assigned: 'Assigned', InProgress: 'In progress', Submitted: 'Submitted' },
        emptyTitle: 'Nothing to evaluate',
        emptyBody: 'Proposals assigned to you for scoring will appear here.',
        progress: '{{done}} of {{total}} scored',
        due: 'Due {{date}}',
        noDueDate: 'No target date',
        score: 'Start scoring',
        review: 'View evaluation',
        loadFailed: "Couldn't load your evaluations",
        retry: 'Try again',
      },
      notifications: {
        title: 'Notifications',
        emptyTitle: 'No notifications yet',
        emptyBody: 'Updates about RFQs, your proposals and awards will appear here.',
        markAllRead: 'Mark all as read',
        markRead: 'Mark as read',
        open: 'Open',
        loadFailed: "Couldn't load notifications",
        retry: 'Try again',
        bell: 'Notifications',
        bellWithCount: 'Notifications, {{count}} unread',
      },
      common: { loading: 'Loading...', cancel: 'Cancel', concurrencyConflict: 'Not saved — someone else changed this first. Please reload and try again.' },
      // See the Arabic block above for why these are transcription, not authorship.
      status: {
        onboarding: {
          Draft: 'Draft', EmailVerified: 'Email verified', ProfileInProgress: 'In progress',
          Submitted: 'Submitted', UnderReview: 'Under review', InfoRequested: 'Info requested',
          Resubmitted: 'Resubmitted', Approved: 'Approved', Rejected: 'Rejected',
          Active: 'Active', Suspended: 'Suspended', Deactivated: 'Deactivated',
        },
        document: {
          Required: 'Required',
          Missing: 'Missing',
          Uploaded: 'Uploaded', UnderReview: 'Under review', Approved: 'Approved',
          Rejected: 'Rejected', ExpiringSoon: 'Expiring soon', Expired: 'Expired',
          PendingScan: 'Scanning', ScanRejected: 'Rejected (virus scan)',
        },
        // §7.6. NotRequested has no entry - see the Arabic block above.
        erpSync: { Requested: 'Sync pending', Synced: 'Synced', Failed: 'Sync failed' },
        invitation: {
          Invited: 'Invited', Viewed: 'Viewed', Responding: 'Responding',
          Submitted: 'Submitted', Declined: 'Declined',
        },
        rfq: {
          Draft: 'Draft', InternalReview: 'Internal review', Approved: 'Approved', Published: 'Published',
          SubmissionOpen: 'Open for submissions', SubmissionClosed: 'Submissions closed',
          UnderEvaluation: 'Under evaluation', Clarification: 'Clarification', Shortlisting: 'Shortlisting',
          Recommendation: 'Recommendation', AwardApproval: 'Award approval', Awarded: 'Awarded',
          Completed: 'Completed', Cancelled: 'Cancelled',
        },
        proposal: {
          Draft: 'Draft', Submitted: 'Submitted', UnderReview: 'Under review',
          ClarificationRequested: 'Clarification requested', Revised: 'Revised', Shortlisted: 'Shortlisted',
          NotSelected: 'Not selected', AwardOffered: 'Award offered', Awarded: 'Awarded',
          Declined: 'Declined', Withdrawn: 'Withdrawn',
          Lapsed: 'Window closed', Cancelled: 'RFQ cancelled',
        },
        evaluation: {
          NotStarted: 'Not started', Assigned: 'Assigned', InProgress: 'In progress',
          EvaluatorSubmitted: 'Submitted', Consolidated: 'Consolidated', Finalized: 'Finalized',
        },
        award: {
          Recommended: 'Recommended', PendingApproval: 'Pending approval', Approved: 'Approved',
          Rejected: 'Rejected', Awarded: 'Awarded',
        },
      },
      phone: {
        countryCode: 'Country code',
        localNumberPlaceholder: 'Phone number',
        other: 'Other',
        countries: {
          SY: 'Syria (+963)',
          JO: 'Jordan (+962)',
          LB: 'Lebanon (+961)',
          IQ: 'Iraq (+964)',
          SA: 'Saudi Arabia (+966)',
          AE: 'UAE (+971)',
          QA: 'Qatar (+974)',
          KW: 'Kuwait (+965)',
          BH: 'Bahrain (+973)',
          OM: 'Oman (+968)',
          PS: 'Palestine (+970)',
          TR: 'Turkey (+90)',
          EG: 'Egypt (+20)',
        },
      },
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
        verifyFailed: 'Could not verify this email - the link is invalid, expired, or already used',
        resendVerification: 'Resend verification link',
        resendSent: 'If that account exists and is unverified, a new verification link has been sent',
        mfaTitle: 'Two-factor verification',
        mfaCodeLabel: 'Authenticator code (6 digits)',
        mfaSubmit: 'Verify',
        mfaInvalid: 'Incorrect code, try again',
        mfaBack: 'Back',
      },
      dashboard: {
        welcome: 'Welcome, {{email}}',
        supplierId: 'Supplier ID',
        permission: 'Current permission',
        placeholder: 'Order and contract summaries will appear here.',
      },
      onboarding: {
        title: 'Complete Your Supplier Profile',
        checklist: 'Requirements checklist',
        submitBlockedTitle: 'Your application cannot be submitted yet',
        submitBlockedIntro: 'These required documents are missing:',
        complete: 'Complete',
        save: 'Save',
        submit: 'Submit application',
        saved: 'Saved',
        saveFailed: 'Could not save',
        submitted: 'Application submitted for review',
        submitFailed: 'Could not submit application',
        incomplete: 'Profile incomplete',
        readOnlyNotice: 'Your application has been submitted and is now read-only.',
        stepNavLabel: 'Onboarding steps',
        steps: { company: 'Company', contacts: 'Contacts', addresses: 'Addresses', banking: 'Banking', offerings: 'Offerings' },
        fields: {
          legalInfo: 'Legal information',
          legalNameAr: 'Legal name (Arabic)',
          legalNameEn: 'Legal name (English)',
          registrationNumber: 'Registration number',
          taxId: 'Tax ID',
          supplierType: 'Entity type',
          establishedOn: 'Established on',
          description: 'Description',
          website: 'Website',
          supplierGroup: 'Group',
          currencyCode: 'Currency',
          address: 'Head office address',
          categoryLink: 'Offered categories',
          primaryContactPhone: "Primary contact's phone",
          contact: 'Contact',
          representative: 'Representative',
          branch: 'Branch',
          bankAccount: 'Bank account',
          logo: 'Company logo',
        },
        supplierTypes: { Company: 'Company', Individual: 'Individual', Partnership: 'Partnership' },
        errors: { legalNameArRequired: 'Legal name (Arabic) is required', legalNameEnRequired: 'Legal name (English) is required' },
        logoTitle: 'Company logo',
        logoAlt: 'Company logo',
        noLogo: 'No logo',
        logoUpload: 'Upload logo',
        logoReplace: 'Replace logo',
        logoUploaded: 'Logo uploaded',
        logoUploadFailed: 'Could not upload logo',
        legalTitle: 'Legal information',
        profileTitle: 'Profile details',
        documents: 'Documents',
        requiredDocuments: 'Required documents',
        optionalDocuments: 'Optional documents',
        noOptionalDocuments: 'No optional documents.',
        optional: 'optional',
        upload: 'Upload',
        reupload: 'Re-upload',
        download: 'Download',
        documentUploaded: 'Document uploaded',
        documentUploadFailed: 'Could not upload document',
        documentExpiryRequired: 'Enter an expiry date before uploading this document',
        documentExpiryLabel: 'Expiry date',
        flagged: 'Needs update',
        infoRequestedTitle: 'Additional information requested',
        resubmit: 'Resubmit',
        resubmitted: 'Application resubmitted for review',
        resubmitFailed: 'Could not resubmit application',
        termsLabel: 'Terms & Conditions accepted',
        termsTitle: 'Terms & Conditions',
        termsCheckboxLabel: 'I confirm I have read and accept the Supplier Portal Terms & Conditions and data-processing notice.',
        termsAccept: 'Accept',
        termsAccepted: 'Terms & Conditions acceptance recorded',
        termsAcceptFailed: 'Could not record acceptance',
        termsAcceptedNotice: 'Version {{version}} of the Terms & Conditions was accepted on {{date}}.',
        conflictTitle: 'Not saved — someone else changed this first',
        conflictBody: 'Another user saved changes to this profile while you were editing. Reload to see the latest data, then re-apply your changes.',
        notFlaggedTitle: 'This field is not part of the information request',
        notFlaggedBody: 'The reviewer asked you to correct specific sections only. This field cannot be edited until the review is complete.',
      },
      contacts: {
        title: 'Contacts & Representatives',
        subtitle: 'A primary representative is authorized to act on the supplier’s behalf; additional contacts are for general communication only.',
        representativesTitle: 'Representatives',
        contactsTitle: 'Additional Contacts',
        addRepresentative: 'Add representative',
        editRepresentative: 'Edit representative',
        addContact: 'Add contact',
        editContact: 'Edit contact',
        primary: 'Primary',
        makePrimary: 'Make primary',
        status: 'Status',
        actions: 'Actions',
        edit: 'Edit',
        remove: 'Remove',
        save: 'Save',
        cancel: 'Cancel',
        empty: 'Nothing here yet.',
        fields: { fullName: 'Full name', email: 'Email', phone: 'Phone', position: 'Position', role: 'Role' },
        errors: { fullNameRequired: 'Full name is required', emailInvalid: 'Enter a valid email', removeFailed: 'Could not remove', setPrimaryFailed: 'Could not set primary representative' },
      },
      addresses: {
        title: 'Addresses & Branches',
        subtitle: 'You need at least one Head Office address to submit your application.',
        addressesTitle: 'Addresses',
        branchesTitle: 'Branches',
        addAddress: 'Add address',
        editAddress: 'Edit address',
        addBranch: 'Add branch',
        editBranch: 'Edit branch',
        actions: 'Actions',
        edit: 'Edit',
        remove: 'Remove',
        save: 'Save',
        cancel: 'Cancel',
        empty: 'No addresses yet.',
        emptyBranches: 'No branches yet.',
        missingHeadOffice: 'You need at least one Head Office address to submit - a Branch or Billing address alone isn’t enough.',
        needHeadOfficeHint: 'You don’t currently have a Head Office address.',
        linkedAddressHint: 'Optional - link this branch to one of your addresses.',
        kinds: { HeadOffice: 'Head Office', Billing: 'Billing', Branch: 'Branch' },
        fields: {
          kind: 'Kind',
          line1: 'Address',
          line2: 'Address line 2',
          city: 'City',
          regionCode: 'Region',
          country: 'Country',
          postalCode: 'Postal code',
          nameAr: 'Branch name (Arabic)',
          nameEn: 'Branch name (English)',
          linkedAddress: 'Linked address',
        },
        errors: {
          line1Required: 'Address is required',
          cityRequired: 'City is required',
          countryRequired: 'Country is required',
          nameArRequired: 'Branch name (Arabic) is required',
          nameEnRequired: 'Branch name (English) is required',
          removeFailed: 'Could not remove',
        },
      },
      banking: {
        title: 'Bank Accounts',
        subtitle: 'The account number is always masked and only revealed on explicit request - every reveal is recorded in the audit log.',
        accountsTitle: 'Bank Accounts',
        addAccount: 'Add account',
        editAccount: 'Edit account',
        actions: 'Actions',
        edit: 'Edit',
        remove: 'Remove',
        save: 'Save',
        cancel: 'Cancel',
        empty: 'No bank accounts yet.',
        reveal: 'Reveal',
        hide: 'Hide',
        revealFailed: 'Could not reveal account number',
        default: 'Default',
        isDefault: 'Default',
        makeDefault: 'Make default',
        accountNumberEditHint: 'Leave blank to keep the current account number unchanged.',
        fields: {
          accountHolderName: 'Account holder name',
          bankName: 'Bank name',
          branchName: 'Branch name',
          accountNumber: 'Account number',
          swiftBic: 'SWIFT/BIC',
          currencyCode: 'Currency',
        },
        errors: { accountHolderRequired: 'Account holder name is required', bankNameRequired: 'Bank name is required', accountNumberRequired: 'Account number is required', removeFailed: 'Could not remove' },
      },
      offerings: {
        title: 'Offered Categories',
        subtitle: 'Select the categories your supplier offers. At least one is required to submit.',
        categoriesTitle: 'Categories',
        empty: 'No categories available yet.',
        missingCategory: 'Select at least one category to submit your application.',
      },
      offeringCatalog: {
        title: 'Offerings',
        subtitle: "Create, edit, and deactivate what your supplier provides to buying entities.",
        listTitle: 'Offerings',
        empty: 'No offerings yet',
        add: 'Add offering',
        createTitle: 'Add offering',
        editTitle: 'Edit offering',
        save: 'Save',
        cancel: 'Cancel',
        edit: 'Edit',
        deactivate: 'Deactivate',
        status: 'Status',
        actions: 'Actions',
        active: 'Active',
        inactive: 'Inactive',
        created: 'Offering created',
        updated: 'Offering updated',
        deactivated: 'Offering deactivated',
        fields: {
          name: 'Name',
          nameAr: 'Name (Arabic)',
          nameEn: 'Name (English)',
          description: 'Description',
          category: 'Category',
          unit: 'Unit of measure',
          price: 'Indicative price',
          currency: 'Currency',
          attributes: 'Additional attributes',
          attributeKey: 'Attribute',
          attributeValue: 'Value',
          addAttribute: 'Add attribute',
          removeAttribute: 'Remove',
        },
        errors: {
          required: 'This field is required',
          invalidCategory: 'Unknown category',
          invalidUnit: 'Unknown unit of measure',
          invalidCurrency: 'Unknown currency',
          saveFailed: 'Could not save the offering',
          deactivateFailed: 'Could not deactivate the offering',
        },
      },
      offeringSearch: {
        title: 'Offering Search',
        subtitle: 'Search active suppliers’ offerings for RFQ invitation candidates.',
        filterCategory: 'Category',
        filterAll: 'All',
        searchPlaceholder: 'Search by name…',
        empty: 'No results',
        supplier: 'Supplier',
        fields: { name: 'Name', category: 'Category', unit: 'Unit', price: 'Price', attributes: 'Additional attributes' },
      },
      evaluationTemplates: {
        title: 'Evaluation Templates',
        subtitle: 'Create and manage reusable weighted evaluation criteria templates.',
        add: 'New Template',
        empty: 'No templates yet',
        createTitle: 'Create Evaluation Template',
        save: 'Save',
        cancel: 'Cancel',
        created: 'Template created',
        criterionAdded: 'Criterion added',
        activated: 'Template activated',
        archived: 'Template archived',
        forked: 'New template version created',
        referenced: 'Bound to an RFQ',
        weightTotal: 'Weight total: {{total}}',
        addCriterion: 'Add criterion',
        activate: 'Activate',
        archive: 'Archive',
        fork: 'Create new version',
        fields: { name: 'Name', nameAr: 'Name (Arabic)', nameEn: 'Name (English)', dimension: 'Dimension', weight: 'Weight', maxScore: 'Max score', scoringType: 'Scoring type' },
        errors: { saveFailed: 'Could not save the template', activateFailed: 'Could not activate the template' },
      },
      rfq: {
        title: 'RFQs',
        subtitle: 'Create and manage Requests for Quotation through their full lifecycle.',
        add: 'New RFQ',
        listTitle: 'RFQ List',
        empty: 'No RFQs yet',
        loadMore: 'Load more',
        createTitle: 'Create RFQ',
        save: 'Save',
        cancel: 'Cancel',
        created: 'RFQ created',
        itemAdded: 'Item added',
        requirementAdded: 'Requirement added',
        templateBound: 'Evaluation template bound',
        submitted: 'RFQ submitted for review',
        returned: 'RFQ returned for edits',
        approved: 'RFQ approved',
        published: 'RFQ published',
        closed: 'Submission window closed',
        cancelled: 'RFQ cancelled',
        submitForReview: 'Submit for review',
        approve: 'Approve',
        publish: 'Publish',
        deadline: {
          title: 'Submission deadline',
          help: 'Extending is the officer\'s; bringing the date forward is the manager\'s. Every invited supplier is notified of either.',
          newDeadline: 'New deadline',
          reason: 'Reason for the change',
          apply: 'Change deadline',
          changed: 'The submission deadline was changed',
          failed: 'Could not change the submission deadline',
        },
        closeSubmission: 'Close submission window',
        manualCloseReason: 'Manually closed by procurement officer',
        returnForEditsTitle: 'Return for edits',
        returnForEdits: 'Return for edits',
        cancelTitle: 'Cancel RFQ',
        cancelRfq: 'Cancel RFQ',
        addItem: 'Add item',
        addRequirement: 'Add requirement',
        bindTemplate: 'Bind template',
        remove: 'Remove',
        actions: 'Actions',
        noItems: 'No items yet',
        attachments: {
          title: 'RFQ attachments',
          none: 'No attachments',
          add: 'Add an attachment',
          added: 'Attachment added',
          download: 'Download',
        },
        noRequirements: 'No requirements yet',
        noTemplateBound: 'No evaluation template bound yet',
        boundTemplate: 'Bound template: {{id}} (version {{version}})',
        yes: 'Yes',
        no: 'No',
        pending: 'Pending',
        fields: {
          reference: 'Reference', title: 'Title', titleAr: 'Title (Arabic)', titleEn: 'Title (English)',
          state: 'State', currency: 'Currency', submissionOpensAt: 'Submission opens', submissionClosesAt: 'Submission closes',
          items: 'Items', requirements: 'Requirements', category: 'Category', unit: 'Unit', quantity: 'Quantity',
          text: 'Text', textAr: 'Text (Arabic)', textEn: 'Text (English)', mandatory: 'Mandatory',
          evaluationTemplate: 'Evaluation template', approvals: 'Approvals', step: 'Step', decision: 'Decision',
          comments: 'Comments', reason: 'Reason',
        },
        errors: { saveFailed: 'Could not save the RFQ', transitionFailed: 'Could not perform the action' },
        invitations: {
          title: 'Invitations',
          none: 'No suppliers invited yet',
          candidatesTitle: 'Suggested suppliers',
          invite: 'Invite',
          invited: 'Supplier invited',
          matchCount: '{{count}} category match(es)',
          fields: { supplier: 'Supplier', status: 'Status', invitedAt: 'Invited', viewedAt: 'Viewed', declineReason: 'Decline reason' },
          errors: { inviteFailed: 'Could not invite the supplier' },
        },
        clarifications: {
          title: 'Clarifications',
          none: 'No questions yet',
          answer: 'Answer',
          answerLabel: 'Answer',
          answered: 'Answer saved',
          publish: 'Publish to all',
          published: 'Published to all',
          private: 'Private to asker',
          publishNow: 'Publish immediately',
          broadcastNotice: 'The answer goes to every invited supplier. The asker is not named.',
          errors: { answerFailed: 'Could not save the answer' },
        },
        addenda: {
          title: 'Addenda',
          none: 'No addenda yet',
          issue: 'Issue addendum',
          issued: 'Addendum issued',
          descriptionAr: 'Description (Arabic)',
          descriptionEn: 'Description (English)',
          errors: { issueFailed: 'Could not issue the addendum' },
        },
      },
      evaluation: {
        title: 'Evaluation',
        open: 'Open evaluation',
        opened: 'Evaluation opened',
        notOpened: 'Evaluation has not been opened yet',
        criteria: 'Criteria',
        dimension: 'Dimension',
        weight: 'Weight',
        threshold: 'Threshold',
        envelope: 'Envelope',
        technicalEnvelope: 'Technical',
        financialEnvelope: 'Financial',
        assignments: 'Assigned evaluators',
        evaluator: 'Evaluator',
        evaluatorUserId: 'Evaluator user id',
        assign: 'Assign',
        assigned: 'Evaluator assigned',
        noAssignments: 'No evaluators assigned yet',
        submittedAt: 'Submitted',
        recusedAt: 'Recused',
        recusedWithReason: 'Recused: {{reason}}',
        recuse: 'Recuse',
        recuseReason: 'Recusal reason',
        confirmRecuse: 'Confirm recusal',
        recused: 'Evaluator recused',
        results: 'Results',
        rank: 'Rank',
        proposal: 'Proposal',
        qualified: 'Technical qualification',
        qualifiedYes: 'Qualified',
        qualifiedNo: 'Not qualified',
        technicalScore: 'Technical score',
        financialScore: 'Financial score',
        total: 'Total',
        consolidate: 'Consolidate',
        consolidated: 'Results consolidated',
        finalize: 'Finalize',
        finalized: 'Evaluation finalized',
        reopen: 'Reopen',
        reopenReason: 'Reason for reopening',
        reopened: 'Evaluation reopened',
        errors: { actionFailed: 'Could not perform the action' },
        my: {
          title: 'My Evaluation',
          notAssigned: 'You are not assigned to this evaluation',
          proposal: 'Proposal',
          score: 'Score',
          scorePlaceholder: 'Enter a score',
          commentAr: 'Comment (Arabic)',
          commentEn: 'Comment (English)',
          save: 'Save score',
          saved: 'Score saved',
          submit: 'Submit evaluation',
          submitted: 'Evaluation submitted',
          alreadySubmitted: 'You have already submitted your evaluation',
          financialLocked: 'The financial envelope is locked until this proposal passes technical qualification',
          qualified: 'Technically qualified',
          notQualified: 'Not technically qualified',
          anonymousBidder: 'Bidder identity withheld during scoring',
          declaration: {
            title: 'Conflict of interest declaration',
            body: 'These are the suppliers taking part. If you have a conflict of interest with any of them, say so now — once you declare, the names are withheld and scoring is anonymous.',
            noConflict: 'No conflict — continue',
            hasConflict: 'I have a conflict — recuse me',
            reasonLabel: 'Reason for recusal',
            reasonPlaceholder: 'Reason for recusal',
            failed: 'Could not record the declaration',
          },
          specification: 'What was requested',
          items: 'Items',
          requirements: 'Requirements',
          mandatory: 'mandatory',
          narrative: 'Technical narrative',
          answers: 'Requirement answers',
          documents: 'Technical documents',
          errors: {
            scoreFailed: 'Could not save the score',
            submitFailed: 'Could not submit the evaluation',
            documentFailed: 'Could not open {{fileName}}',
          },
        },
      },
      comparison: {
        clarifyTitle: 'Ask a bidder to clarify',
        clarifyBody: 'Ask a supplier to explain something about their bid. They are notified and the proposal returns to Clarification requested.',
        clarifyReason: 'What to clarify for {{code}}',
        clarifyReasonPlaceholder: 'What needs clarifying',
        clarifyAsk: 'Request clarification',
        clarifyRequested: 'The clarification request was sent',
        clarifyFailed: 'Could not send the clarification request',
        tieUnresolved: 'Unresolved tie',
        tieTitle: 'A tie in the ranking needs a decision',
        tieBody: 'These bids are equal on every tie-break rule. Choose the one that ranks first and say why; no award can be recommended until you do.',
        tieReason: 'Reason for choosing {{code}}',
        tieReasonPlaceholder: 'Reason for the decision',
        tieResolve: 'Confirm the order',
        tieResolved: 'The order is confirmed',
        tieResolveFailed: 'Could not confirm the order',
        title: 'Proposal Comparison',
        notFound: 'Comparison not available',
        empty: 'No proposals submitted yet',
        proposalCount: '{{count}} proposal',
        proposalCount_other: '{{count}} proposals',
        awaitingConsolidation: 'Awaiting evaluation consolidation',
        rowLabel: 'Line item',
        notVisible: 'Not visible',
        groups: { commercial: 'Commercial', requirements: 'Requirements', evaluation: 'Evaluation' },
        grandTotal: 'Grand total',
        paymentTerms: 'Payment terms',
        incoterm: 'Incoterm',
        validityEnd: 'Validity end',
        met: 'Met',
        notMet: 'Not met',
        pass: 'Pass',
        fail: 'Fail',
        qualification: 'Technical qualification',
        qualified: 'Qualified',
        notQualified: 'Not qualified',
        weightedTotal: 'Weighted total',
        rank: 'Rank',
      },
      workspace: {
        title: 'RFQ Workflow',
        stages: 'Lifecycle stages',
        noNextAction: 'No next action is currently available.',
        cancelledBanner: 'This RFQ has been cancelled.',
      },
      award: {
        title: 'Award',
        status: 'Award status',
        notRecommendedYet: 'No winner recommended yet',
        recommend: 'Recommend winner',
        reRecommend: 'Re-recommend',
        selectWinner: 'Select the winning proposal',
        winnerOption: 'Rank {{rank}} — weighted total {{total}}',
        noQualifiedProposals: 'No technically qualified proposals yet',
        justification: 'Justification',
        justificationAr: 'Justification (Arabic)',
        justificationEn: 'Justification (English)',
        revision: 'Revision {{count}}',
        recommended: 'Recommendation recorded',
        routeForApproval: 'Route for approval',
        routed: 'Recommendation routed for approval',
        approvals: 'Approval steps',
        stepLabel: 'Step {{step}}',
        pending: 'Pending',
        approve: 'Approve',
        approved: 'Award approved',
        reject: 'Reject',
        rejectReason: 'Rejection reason',
        rejected: 'Recommendation rejected',
        rejectionReason: 'Rejection reason',
        execute: 'Issue award',
        issued: 'Award issued',
        erpStatus: 'ERP sync status',
        externalPoRef: 'Purchase order reference',
        retrySync: 'Retry sync',
        retryQueued: 'Sync retry queued',
        errors: { actionFailed: 'Could not perform the action', segregationOfDuties: 'The approver must differ from the recommender' },
      },
      supplierRfq: {
        deadlineChanged: {
          title: 'The submission deadline changed',
        },
        attachments: {
          title: 'RFQ attachments',
          none: 'No attachments',
          download: 'Download',
          downloadFailed: 'Could not download the attachment',
        },
        title: 'RFQs',
        subtitle: 'Requests for Quotation you have been invited to.',
        listTitle: 'Invitations',
        empty: 'No invitations yet',
        loadMore: 'Load more',
        myStatus: 'My status',
        notFound: 'RFQ not found',
        declineTitle: 'Decline invitation',
        decline: 'Decline invitation',
        declineReasonPlaceholder: 'Reason (optional)',
        declined: 'Invitation declined',
        errors: { declineFailed: 'Could not decline the invitation' },
        clarifications: {
          title: 'Clarifications',
          none: 'No questions yet',
          mine: 'My question',
          awaitingAnswer: 'Awaiting answer',
          askPlaceholder: 'Type your question…',
          ask: 'Send question',
          asked: 'Question sent',
          errors: { askFailed: 'Could not send the question' },
        },
      },
      proposal: {
        title: 'Proposal',
        conflictTitle: 'This proposal changed somewhere else',
        conflictBody: 'Another user - or another tab - changed this proposal after you opened it. Reload to see the current version before saving your changes.',
        conflictReload: 'Reload',
        start: 'Start proposal',
        goToMyProposal: 'Go to my proposal',
        submit: 'Submit proposal',
        submitted: 'Proposal submitted',
        pricing: 'Pricing',
        unitPrice: 'Unit price',
        lineTotal: 'Line total',
        savePrice: 'Save price',
        itemPriced: 'Item price saved',
        requirements: 'Requirements',
        noRequirements: 'No requirements',
        saveAnswer: 'Save answer',
        requirementAnswered: 'Answer saved',
        terms: 'Commercial terms',
        currency: 'Currency',
        paymentTerms: 'Payment terms',
        incoterm: 'Incoterm',
        validityEnd: 'Validity end date',
        saveTerms: 'Save terms',
        termsSaved: 'Terms saved',
        documents: 'Documents',
        noDocuments: 'No documents yet',
        envelope: 'Envelope',
        envelopeCommercial: 'Commercial envelope',
        envelopeTechnical: 'Technical envelope',
        envelopeExpected: {
          Technical: 'This document is expected in the technical envelope.',
          Commercial: 'This document is expected in the commercial envelope.',
        },
        uploadDocument: 'Upload document',
        documentAdded: 'Document added',
        withdrawTitle: 'Withdraw proposal',
        withdraw: 'Withdraw proposal',
        withdrawReasonPlaceholder: 'Reason for withdrawal',
        withdrawn: 'Proposal withdrawn',
        awardOfferedTitle: 'Award offer',
        awardOfferedBody: 'Your proposal has been selected for award. You may decline with a reason, or wait for the buyer to confirm.',
        decline: 'Decline the award',
        declineReason: 'Reason for declining',
        declineReasonPlaceholder: 'Why you are declining the award',
        declined: 'Your decline has been recorded',
        errors: {
          startFailed: 'Could not start the proposal', saveFailed: 'Could not save',
          submitFailed: 'Could not submit the proposal', withdrawFailed: 'Could not withdraw the proposal',
          declineFailed: 'Could not record the decline',
        },
      },
      team: {
        title: 'Team Management',
        subtitle: 'Invite additional users to access and manage your supplier profile.',
        membersTitle: 'Team Members',
        loadMore: 'Load more',
        invite: 'Invite member',
        inviteTitle: 'Invite a new member',
        inviteDescription: 'The invited member will receive an email with a link to set their password.',
        sendInvite: 'Send invite',
        cancel: 'Cancel',
        inviteSent: 'Invite sent',
        inviteFailed: 'Could not send invite',
        duplicateEmail: 'An account with that email already exists',
        disable: 'Disable',
        disabled: 'Member disabled',
        disableFailed: 'Could not disable member',
        empty: 'No team members yet.',
        status: 'Status',
        actions: 'Actions',
        active: 'Active',
        disabledStatus: 'Disabled',
        fields: { fullName: 'Full name', email: 'Email' },
        errors: { fullNameRequired: 'Full name is required', emailInvalid: 'Enter a valid email', passwordTooShort: 'Password is too short' },
        acceptInviteTitle: 'Accept invite',
        acceptInviteHint: 'Set a password to finish joining the supplier team.',
        acceptInviteSubmit: 'Accept invite',
        acceptInviteSuccess: 'Invite accepted. You can now sign in.',
        acceptInviteInvalid: 'This link is invalid or has expired',
      },
      review: {
        title: 'Supplier Application Review',
        queue: 'Review queue',
        age: 'Age',
        reviewTarget: 'Target date',
        assignee: 'Assignee',
        actions: 'Actions',
        filterState: 'State',
        filterAssignee: 'Assignee',
        filterAll: 'All',
        claim: 'Claim',
        claimed: 'Claimed',
        claimFailed: 'Could not claim',
        unassign: 'Unassign',
        unassigned: 'Unassigned',
        unassignFailed: 'Could not unassign',
        unassignedLabel: 'Unassigned',
        assignedToMe: 'Assigned to me',
        assignedToOther: 'Assigned to another reviewer',
        noItems: 'No applications awaiting review',
        loadMore: 'Load more',
        backToQueue: 'Back to queue',
        pickUp: 'Start review',
        pickUpFailed: 'Could not start review',
        approve: 'Approve',
        approveFailed: 'Could not approve',
        reject: 'Reject',
        rejectFailed: 'Could not reject',
        suspend: 'Suspend',
        reactivate: 'Reactivate',
        deactivate: 'Deactivate',
        deactivateWarning: 'Deactivation is permanent and cannot be undone. All of this supplier\u2019s users will lose access immediately.',
        lifecycleFailed: 'Could not change supplier state',
        requestInfo: 'Request info',
        requestInfoFailed: 'Could not submit info request',
        reason: 'Reason',
        flagProfileFields: 'Fields needing changes',
        flagDocuments: 'Documents needing changes',
        submit: 'Submit',
        cancel: 'Cancel',
        profile: 'Profile',
        legalInfo: 'Legal information',
        addresses: 'Addresses',
        representatives: 'Representatives',
        contacts: 'Additional contacts',
        documents: 'Documents',
        annotationHistory: 'Info request history',
        decisionSuccess: 'Decision recorded',
      },
      staff: {
        title: 'Staff',
        subtitle: 'Invite new back-office staff members and assign their role.',
        invite: 'Invite',
        inviteTitle: 'Invite staff member',
        hint: 'Invite a new staff member by email and role. They will get a link to set their own password.',
        cancel: 'Cancel',
        invited: 'Invite sent to {{email}}',
        fields: { fullName: 'Full name', email: 'Email', role: 'Role' },
        roles: {
          onboarding_reviewer: 'Onboarding Reviewer',
          procurement_officer: 'Procurement Officer',
          procurement_manager: 'Procurement Manager',
          evaluator: 'Evaluator',
          ministry_viewer: 'Ministry Viewer',
          system_admin: 'System Admin',
        },
        errors: {
          fullNameRequired: 'Full name is required',
          emailInvalid: 'Email is invalid',
          inviteFailed: 'Could not send invite',
          loadFailed: 'Could not load the staff accounts',
          updateFailed: 'Could not complete that action',
          cannotActOnSelf: 'You cannot do that to your own account.',
          wouldLockOutAdministration: 'The last active system administrator cannot be deactivated.',
          passwordTooShort: 'Password is too short',
        },
        accountsTitle: 'Staff accounts',
        noAccounts: 'No accounts',
        status: 'Status',
        actions: 'Actions',
        active: 'Active',
        inactive: 'Deactivated',
        mfaOn: 'Two-factor enrolled',
        sessions: 'Active sessions: {{count}}',
        deactivate: 'Deactivate',
        reactivate: 'Reactivate',
        resetMfa: 'Reset two-factor',
        roleChanged: 'The role was changed',
        mfaReset: 'Two-factor was reset',
        retry: 'Try again',
        acceptInviteTitle: 'Accept staff invite',
        acceptInviteHint: 'Set a password to complete your account setup.',
        acceptInviteSuccess: 'Your account is set up. You can now sign in.',
        acceptInviteInvalid: 'This invite link is invalid, expired, or already used.',
        acceptInviteSubmit: 'Set password',
      },
      roleManagement: {
        title: 'Roles & Permissions',
        subtitle: "Edit each role's permission set. Changes take effect on that role's users' next login.",
        errors: {
          invalidPermission: 'Unrecognized permission',
          wouldLockOutRoleManagement: "Can't remove this - no one would ever be able to edit roles again",
          updateFailed: 'Could not save the change',
        },
      },
      organizations: {
        title: 'Organizations',
        subtitle: 'Create Organizations and manage their links to Suppliers (manual only, no auto-linking).',
        listTitle: 'Organizations',
        empty: 'No Organizations yet',
        createTitle: 'Create Organization',
        save: 'Save',
        cancel: 'Cancel',
        add: 'Add',
        remove: 'Remove',
        lookup: 'Look up',
        manageOrgUnits: 'Manage units',
        orgUnitsCount: 'Unit count',
        orgUnitsTitle: '{{name}} units',
        noOrgUnits: 'No units yet',
        actions: 'Actions',
        created: 'Organization created',
        linksTitle: 'Supplier Organization Links',
        linkAdd: 'Add link',
        linkCreated: 'Link created',
        noLinks: 'No links for this supplier',
        types: { Hotel: 'Hotel', MotBody: 'MOT-affiliated body', Ministry: 'Ministry' },
        fields: {
          legalNameAr: 'Legal name (Arabic)',
          legalNameEn: 'Legal name (English)',
          organizationType: 'Organization type',
          contactEmail: 'Contact email',
          contactPhone: 'Contact phone',
          orgUnitName: 'Unit name',
          supplierReferenceCode: 'Supplier reference code',
          organization: 'Organization',
        },
        errors: {
          nameRequired: 'Name is required',
          createFailed: 'Could not create the Organization',
          orgUnitFailed: 'Could not complete the unit action',
          linkFailed: 'Could not complete the link action',
        },
      },
      register: {
        title: 'Register a New Supplier',
        displayNameAr: 'Company name (Arabic)',
        displayNameEn: 'Company name (English)',
        registrationNumber: 'Registration number',
        registrationNumberHint: 'Optional at registration, required later to complete your profile',
        representativeName: 'Primary representative name',
        representativePhone: "Primary representative's phone",
        confirmPassword: 'Confirm password',
        submit: 'Create account',
        haveAccount: 'Already have an account? Sign in',
        createAccount: 'Create a new account',
        successTitle: 'Account created',
        closedTitle: 'Registration is closed',
        closedBody: 'Self-registration is currently closed. Contact the Ministry to be onboarded.',
        checkEmail: 'Check your email to verify your account. Your reference code:',
        duplicateEmail: 'An account with that email already exists',
        weakPassword: "Password doesn't meet the strength requirements",
        failed: 'Could not create the account',
      },
      settings: {
        title: 'Account Settings',
        mfaTitle: 'Two-factor authentication',
        mfaEnroll: 'Enable two-factor authentication',
        mfaScanOrEnter: 'Scan with your authenticator app or enter the key manually',
        mfaCodeLabel: 'Verification code',
        mfaConfirm: 'Confirm',
        mfaEnabled: 'Two-factor authentication enabled',
        mfaEnrollFailed: 'Could not start enrollment',
        mfaInvalidCode: 'Invalid code',
        recoveryCodesNotice: 'Save these recovery codes somewhere safe - they will not be shown again.',
        sessionsTitle: 'Active sessions',
        auditTitle: 'My account activity',
        auditHint: 'The most recent events recorded against your account, newest first.',
        auditEmpty: 'No events recorded yet',
        auditExport: 'Download the trail (CSV)',
        auditExportFailed: 'Could not download the trail',
        auditLoadFailed: 'Could not load the trail',
        retry: 'Try again',
        currentSession: 'Current session',
        unknownDevice: 'Unknown device',
        revoke: 'Sign out',
        loadMoreSessions: 'Load more',
        revokeAllOthers: 'Sign out of all other devices',
        sessionRevoked: 'Session signed out',
        sessionsRevokedAll: 'Signed out of all other devices',
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
