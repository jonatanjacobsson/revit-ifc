//
// BIM IFC library: this library works with Autodesk(R) Revit(R) to export IFC files containing model geometry.
// Copyright (C) 2012  Autodesk, Inc.
// Modified: UseMEPSystemTypeGraphicOverrides (jonatanjacobsson fork)
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Revit.IFC.Common.Utility;
using Revit.IFC.Export.Toolkit;

namespace Revit.IFC.Export.Utility
{
   /// <summary>
   /// Resolves MEP system type LineColor / FillColor graphic overrides for IFC presentation
   /// when ExportOptionsCache.UseMEPSystemTypeGraphicOverrides is enabled.
   /// Styles are attached as IfcStyledItem and do not replace IfcMaterial.
   /// </summary>
   public static class MEPSystemTypeColorUtil
   {
      /// <summary>
      /// Cache of IfcSurfaceStyle handles keyed by packed RGB (0xRRGGBB).
      /// </summary>
      private static readonly Dictionary<int, IFCAnyHandle> RgbToSurfaceStyleCache = new Dictionary<int, IFCAnyHandle>();

      /// <summary>
      /// Cache of presentation handles for StyledItem.Styles (IfcSurfaceStyle on IFC4+,
      /// IfcPresentationStyleAssignment on IFC2x3) keyed by packed RGB.
      /// </summary>
      private static readonly Dictionary<int, IFCAnyHandle> RgbToPresentationCache = new Dictionary<int, IFCAnyHandle>();

      /// <summary>
      /// Per-element resolved color for the current export. Avoids repeated system-type / connector walks.
      /// </summary>
      private static readonly Dictionary<ElementId, Color> ElementColorCache = new Dictionary<ElementId, Color>();

      /// <summary>
      /// Elements already resolved with no usable graphic override color.
      /// </summary>
      private static readonly HashSet<ElementId> ElementNoColorCache = new HashSet<ElementId>();

      /// <summary>
      /// Per-element MEPSystemType (null stored as missing from both maps below).
      /// </summary>
      private static readonly Dictionary<ElementId, MEPSystemType> ElementSystemTypeCache = new Dictionary<ElementId, MEPSystemType>();
      private static readonly HashSet<ElementId> ElementNoSystemTypeCache = new HashSet<ElementId>();

      private static readonly HashSet<long> PipeRelatedCategories = new HashSet<long>
      {
         (long)BuiltInCategory.OST_PipeCurves,
         (long)BuiltInCategory.OST_FlexPipeCurves,
         (long)BuiltInCategory.OST_PipeFitting,
         (long)BuiltInCategory.OST_PipeAccessory,
         (long)BuiltInCategory.OST_PipeInsulations,
         (long)BuiltInCategory.OST_PlaceHolderPipes,
         (long)BuiltInCategory.OST_PlumbingFixtures,
         (long)BuiltInCategory.OST_Sprinklers,
         (long)BuiltInCategory.OST_AnalyticalPipeConnections
      };

      private static readonly HashSet<long> DuctRelatedCategories = new HashSet<long>
      {
         (long)BuiltInCategory.OST_DuctCurves,
         (long)BuiltInCategory.OST_FlexDuctCurves,
         (long)BuiltInCategory.OST_DuctFitting,
         (long)BuiltInCategory.OST_DuctAccessory,
         (long)BuiltInCategory.OST_DuctInsulations,
         (long)BuiltInCategory.OST_DuctLinings,
         (long)BuiltInCategory.OST_DuctTerminal,
         (long)BuiltInCategory.OST_PlaceHolderDucts
      };

      public static void Clear()
      {
         RgbToSurfaceStyleCache.Clear();
         RgbToPresentationCache.Clear();
         ElementColorCache.Clear();
         ElementNoColorCache.Clear();
         ElementSystemTypeCache.Clear();
         ElementNoSystemTypeCache.Clear();
      }

      public static bool IsEnabled()
      {
         return ExporterCacheManager.ExportOptionsCache?.UseMEPSystemTypeGraphicOverrides ?? false;
      }

      /// <summary>
      /// Packs RGB into a positive int for cache / TypeObjectKey discrimination.
      /// Returns ElementId.InvalidElementId when no override.
      /// </summary>
      public static ElementId GetGraphicOverrideCacheKey(Element element)
      {
         if (!TryGetGraphicOverrideColor(element, out Color color))
            return ElementId.InvalidElementId;

         int packed = PackRgb(color);
         return new ElementId(-(1L << 24) - packed);
      }

      public static int PackRgb(Color color)
      {
         return (color.Red << 16) | (color.Green << 8) | color.Blue;
      }

      /// <summary>
      /// Prefer FillColor for solids; fall back to LineColor.
      /// Results are cached per element for the export run.
      /// </summary>
      public static bool TryGetGraphicOverrideColor(Element element, out Color color)
      {
         color = null;
         if (!IsEnabled() || element == null)
            return false;

         ElementId elementId = element.Id;
         if (ElementColorCache.TryGetValue(elementId, out color))
            return true;
         if (ElementNoColorCache.Contains(elementId))
            return false;

         MEPSystemType systemType = GetMEPSystemType(element);
         if (systemType == null)
         {
            ElementNoColorCache.Add(elementId);
            return false;
         }

         try
         {
            Color fill = systemType.FillColor;
            if (fill != null && fill.IsValid)
            {
               color = fill;
               ElementColorCache[elementId] = color;
               return true;
            }
         }
         catch
         {
         }

         try
         {
            Color line = systemType.LineColor;
            if (line != null && line.IsValid)
            {
               color = line;
               ElementColorCache[elementId] = color;
               return true;
            }
         }
         catch
         {
         }

         ElementNoColorCache.Add(elementId);
         return false;
      }

      public static MEPSystemType GetMEPSystemType(Element element)
      {
         if (element == null)
            return null;

         ElementId elementId = element.Id;
         if (ElementSystemTypeCache.TryGetValue(elementId, out MEPSystemType cached))
            return cached;
         if (ElementNoSystemTypeCache.Contains(elementId))
            return null;

         MEPSystemType resolved = ResolveMEPSystemType(element);
         if (resolved != null)
            ElementSystemTypeCache[elementId] = resolved;
         else
            ElementNoSystemTypeCache.Add(elementId);
         return resolved;
      }

      private static MEPSystemType ResolveMEPSystemType(Element element)
      {
         if (element is MEPSystemType asType)
            return asType;

         Document doc = element.Document;
         ElementId systemTypeId = ElementId.InvalidElementId;
         ElementId categoryId = CategoryUtil.GetSafeCategoryId(element);
         long categoryValue = categoryId?.Value ?? -1;

         if (PipeRelatedCategories.Contains(categoryValue))
         {
            ParameterUtil.GetElementIdValueFromElement(element,
               BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM, out systemTypeId);
         }
         else if (DuctRelatedCategories.Contains(categoryValue))
         {
            ParameterUtil.GetElementIdValueFromElement(element,
               BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM, out systemTypeId);
         }

         if (systemTypeId != ElementId.InvalidElementId)
            return doc.GetElement(systemTypeId) as MEPSystemType;

         // Pipe/duct system instances — cheap check before connector walk.
         if (element is MEPSystem mepSystem)
         {
            ElementId typeId = mepSystem.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
               return doc.GetElement(typeId) as MEPSystemType;
         }

         // Fittings / accessories: walk connectors for an MEPSystem (expensive; cached above).
         FamilyInstance fi = element as FamilyInstance;
         if (fi?.MEPModel?.ConnectorManager != null)
         {
            try
            {
               foreach (Connector connector in fi.MEPModel.ConnectorManager.Connectors)
               {
                  MEPSystem system = connector?.MEPSystem;
                  if (system == null)
                     continue;
                  ElementId typeId = system.GetTypeId();
                  if (typeId == ElementId.InvalidElementId)
                     continue;
                  MEPSystemType st = doc.GetElement(typeId) as MEPSystemType;
                  if (st != null)
                     return st;
               }
            }
            catch
            {
            }
         }

         return null;
      }

      /// <summary>
      /// Creates or reuses an IfcSurfaceStyle for the given RGB color.
      /// </summary>
      public static IFCAnyHandle GetOrCreateSurfaceStyleForColor(IFCFile file, Color color)
      {
         if (file == null || color == null || !color.IsValid)
            return null;

         int packed = PackRgb(color);
         if (RgbToSurfaceStyleCache.TryGetValue(packed, out IFCAnyHandle cached) &&
             !IFCAnyHandleUtil.IsNullOrHasNoValue(cached))
         {
            return cached;
         }

         Color safe = CategoryUtil.GetSafeColor(color);
         double blueVal = safe.Blue / 255.0;
         double greenVal = safe.Green / 255.0;
         double redVal = safe.Red / 255.0;

         IFCAnyHandle colorHnd = IFCInstanceExporter.CreateColourRgb(file, null, redVal, greenVal, blueVal);
         IFCData smoothness = IFCDataUtil.CreateAsNormalisedRatioMeasure(0.0);
         IFCData specularExp = IFCDataUtil.CreateAsSpecularExponent(0);
         IFCReflectanceMethod method = IFCReflectanceMethod.NotDefined;

         IFCAnyHandle renderingHnd = IFCInstanceExporter.CreateSurfaceStyleRendering(file, colorHnd, 0.0,
             null, null, null, null, smoothness, specularExp, method);

         ISet<IFCAnyHandle> surfStyles = new HashSet<IFCAnyHandle>() { renderingHnd };
         string styleName = string.Format("MEPSystemTypeGraphic_{0:X6}", packed);
         IFCAnyHandle styleHnd = IFCInstanceExporter.CreateSurfaceStyle(file, styleName,
            IFCSurfaceSide.Both, surfStyles);

         RgbToSurfaceStyleCache[packed] = styleHnd;
         return styleHnd;
      }

      /// <summary>
      /// Presentation handle for IfcStyledItem.Styles: surface style on IFC4+,
      /// presentation-style assignment on IFC2x3. Cached per RGB.
      /// </summary>
      public static IFCAnyHandle GetOrCreatePresentationForColor(IFCFile file, Color color)
      {
         if (file == null || color == null || !color.IsValid)
            return null;

         int packed = PackRgb(color);
         if (RgbToPresentationCache.TryGetValue(packed, out IFCAnyHandle cached) &&
             !IFCAnyHandleUtil.IsNullOrHasNoValue(cached))
         {
            return cached;
         }

         IFCAnyHandle surfStyleHnd = GetOrCreateSurfaceStyleForColor(file, color);
         if (IFCAnyHandleUtil.IsNullOrHasNoValue(surfStyleHnd))
            return null;

         IFCAnyHandle presentationHnd = surfStyleHnd;
         if (ExporterCacheManager.ExportOptionsCache.ExportAsOlderThanIFC4)
         {
            ISet<IFCAnyHandle> styles = new HashSet<IFCAnyHandle>() { surfStyleHnd };
            presentationHnd = IFCInstanceExporter.CreatePresentationStyleAssignment(file, styles);
         }

         RgbToPresentationCache[packed] = presentationHnd;
         return presentationHnd;
      }
   }
}
