Reply-all to Sripathi's 2026-10-12 email ("Design and Make Marketplace : Civil 3D : Survey & Parcel Field Kit / HydroComplete"). Send after both apps are resubmitted.

---

Hi Sripathi,

Thank you for the review, and sorry for the slow reply. I've made the changes to both apps and resubmitted them:

1. Icons: both icons are now graphics only, with no text.
2. Civil 3D 2027: HydroComplete 1.9.0 adds a build compiled against the Civil 3D 2027 .NET API (.NET 10), which loads on 2027 alongside the existing 2024-2026 builds. Survey & Parcel Field Kit 1.0.1 now declares 2023-2027.
3. App names: "for Civil 3D" is removed. The apps are now "HydroComplete" and "Survey & Parcel Field Kit".
4. Black Duck findings (HydroComplete): every bundled .NET component is updated to its current release (10.0.14, or the latest 4.6.x / 6.1.x for the .NET Framework support libraries used on Civil 3D 2024), and components the add-in didn't use are no longer included. The Field Kit is AutoLISP only, with no .NET components.

Please let me know if the rescan shows anything else.

Thank you,
Michael Flynn
HydroComplete
