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
      /// Cleared with the rest of the export caches.
      /// </summary>
      public static IDictionary<int, IFCAnyHandle> RgbToSurfaceStyleCache { get; private set; }
         = new Dictionary<int, IFCAnyHandle>();

      public static void Clear()
      {
         RgbToSurfaceStyleCache.Clear();
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

         // Use a negative ElementId value space so we never collide with real element ids.
         // ElementId supports long values; we encode 0x01RRGGBB as a negative id key via Value.
         int packed = PackRgb(color);
         return new ElementId(-(1L << 24) - packed);
      }

      public static int PackRgb(Color color)
      {
         return (color.Red << 16) | (color.Green << 8) | color.Blue;
      }

      /// <summary>
      /// Prefer FillColor for solids; fall back to LineColor.
      /// </summary>
      public static bool TryGetGraphicOverrideColor(Element element, out Color color)
      {
         color = null;
         if (!IsEnabled() || element == null)
            return false;

         MEPSystemType systemType = GetMEPSystemType(element);
         if (systemType == null)
            return false;

         try
         {
            Color fill = systemType.FillColor;
            if (fill != null && fill.IsValid)
            {
               color = fill;
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
               return true;
            }
         }
         catch
         {
         }

         return false;
      }

      public static MEPSystemType GetMEPSystemType(Element element)
      {
         if (element == null)
            return null;

         if (element is MEPSystemType asType)
            return asType;

         Document doc = element.Document;
         ElementId systemTypeId = ElementId.InvalidElementId;
         ElementId categoryId = CategoryUtil.GetSafeCategoryId(element);
         long categoryValue = categoryId?.Value ?? -1;

         if (IsPipeRelated(categoryValue))
         {
            ParameterUtil.GetElementIdValueFromElement(element,
               BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM, out systemTypeId);
         }
         else if (IsDuctRelated(categoryValue))
         {
            ParameterUtil.GetElementIdValueFromElement(element,
               BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM, out systemTypeId);
         }

         if (systemTypeId != ElementId.InvalidElementId)
         {
            return doc.GetElement(systemTypeId) as MEPSystemType;
         }

         // Fittings / accessories: walk connectors for an MEPSystem.
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

         // Pipe/duct system instances
         if (element is MEPSystem mepSystem)
         {
            ElementId typeId = mepSystem.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
               return doc.GetElement(typeId) as MEPSystemType;
         }

         return null;
      }

      private static bool IsPipeRelated(long categoryValue)
      {
         return categoryValue == (long)BuiltInCategory.OST_PipeCurves ||
            categoryValue == (long)BuiltInCategory.OST_FlexPipeCurves ||
            categoryValue == (long)BuiltInCategory.OST_PipeFitting ||
            categoryValue == (long)BuiltInCategory.OST_PipeAccessory ||
            categoryValue == (long)BuiltInCategory.OST_PipeInsulations ||
            categoryValue == (long)BuiltInCategory.OST_PlaceHolderPipes ||
            categoryValue == (long)BuiltInCategory.OST_PlumbingFixtures ||
            categoryValue == (long)BuiltInCategory.OST_Sprinklers ||
            categoryValue == (long)BuiltInCategory.OST_AnalyticalPipeConnections;
      }

      private static bool IsDuctRelated(long categoryValue)
      {
         return categoryValue == (long)BuiltInCategory.OST_DuctCurves ||
            categoryValue == (long)BuiltInCategory.OST_FlexDuctCurves ||
            categoryValue == (long)BuiltInCategory.OST_DuctFitting ||
            categoryValue == (long)BuiltInCategory.OST_DuctAccessory ||
            categoryValue == (long)BuiltInCategory.OST_DuctInsulations ||
            categoryValue == (long)BuiltInCategory.OST_DuctLinings ||
            categoryValue == (long)BuiltInCategory.OST_DuctTerminal ||
            categoryValue == (long)BuiltInCategory.OST_PlaceHolderDucts;
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
   }
}
