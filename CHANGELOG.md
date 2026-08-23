# Changelog

Resumen de todos los cambios relevantes del proyecto.

## v0.4.1

- Actualizado el corte del corpus a **567.505/814.328 líneas traducibles (69,7%)** y
  **4.157/6.988 hojas completadas (59,5%)**. El recuento incorpora dos hojas que estaban fuera
  del seguimiento por dominio: `CharaMakeName` y `KTGTypeWordTextData`.
- *Stormblood* sube al **56,3 %**: **17.854/31.689 líneas traducidas**, frente a las
  13.112 de v0.4.0.
- Cerrados tres nuevos lotes de historia principal de *Stormblood* (`quest-stmbda-msq-002`, `-003` y `-004`):
  **135 hojas** de guion; también se completa un lote de **12 hojas** de
  eventos estacionales.
- Revisadas las últimas filas pendientes de validación y corregido el texto de un objeto de misión:
  «registro muy desgastado».
- Traducidas algunas cosas que quedaron pendientes del evento de la Verbena de este año (las conversaciones con las Pregoneras)

## v0.4.0

- *Stormblood* entra en juego: la expansión pasa del **0,0 % al 41,4 %**, con
  **13.112/31.689 líneas traducidas** entre historia principal, secundarias, tribales,
  cinemáticas y conversaciones de NPC.
- Traducido el primer gran tramo de la historia principal de *Stormblood* (**70 hojas**,
  **3.000 filas**): la Marca de Rhalgr, los Confines, los Picos, el viaje a Kugane, el Mar Rubí y
  la Confederación, Sui-no-Sato, Susano, Yanxia y la Casa de los Fieros, la infiltración en el
  castillo de Doma, la Estepa de Azim, Reunión, los Mol y el Trono del Alba.
- Completadas las **11 hojas** de misiones secundarias de *Stormblood* (**469 filas**) y las
  **40 hojas** de las tribus **kojin** y **ananta** (**1.821 filas**).
- Completadas **todas las conversaciones regionales y de Eureka de *Stormblood***:
  **119/119 hojas** y **4.392/4.392 filas (100,0%)**
- Nuevo lote de eventos estacionales pasados.
- Traducidas **11 hojas** de misiones secundarias variadas
- Corregido un fallo del empaquetador que dejaba sin traducir filas idénticas byte a byte: los
  payloads traducidos ahora se propagan a todas sus gemelas deduplicadas.
- Barridos globales de coherencia
- Cambios varios
- Progreso global: **559.353/808.958 líneas traducibles (69,1%)** y **3.989/6.986 hojas completadas
  (57,1%)**, frente al 67,5% y 53,1% de v0.3.4. En total, **+13.529 líneas** y **+279 hojas**.
- Actualizado el SDK de .NET a 10.0.400 y las dependencias de pruebas (xUnit runner 4.0.0,
  Microsoft.NET.Test.Sdk 18.9.0).

  ## v0.3.5

- Actualizado patcher a la versión 2026.08.11.0000.0000 de FFXIV para que no os salga el warning por usar versiones diferentes
- Sin más cambios reales respecto a la v0.3.4

## v0.3.4

- ¡Nuevo hito alcanzado! ¡TODO el contenido de *Heavensward* está totalmente traducido! ⚔️🥳
  **25.274/25.274 líneas (100,0%)** entre historia principal, secundarias, tribales, cinemáticas y
  conversaciones de NPC de la expansión.
- Cerrada la historia principal de *Heavensward*: **472/472 hojas** de MSQ completadas, incluido el
  tramo final `HeaVnb`, `HeaVnc`, `HeaVnd`, `HeaVne`, `HeaVnf`, `HeaVng`, `HeaVnm`, `HeaVnr` y
  `HeaVny`.
- Traducidos **todos los eventos estacionales y colaboraciones**: Heavensturn, Valentione, Little
  Ladies' Day, Hatching-tide, Moonfire Faire, The Rising, All Saints' Wake y Starlight
  (**4.144 filas**), más el cierre de los eventos pendientes de *Heavensward* (`FesXms`, `FesAnv`,
  `FesPdy`, **625 filas**).
- Nuevo lote de diálogos de contenido: Palacio de los Muertos, Cielo Empíreo, Eureka Orthos, marcas
  élite de *Dawntrail*, NPC de mago azul, Isla Santuario, revanchas del Arcadion, trovador errante de
  *Dawntrail* y entregas personalizadas de Anden y Margrat.
- Corpus actualizado a la versión de juego **2026.08.05.0000.0000**, con el delta del parche
  incorporado y el nuevo banner de reputación traducido.
- Barridos de coherencia y terminología: nombre de la Agencia de Viajes de Tural unificado en todo el
  corpus y ratificadas 147 acuñaciones goberas; el corpus queda sin filas pendientes de revisión.
- Progreso global: **545.824/808.960 líneas traducibles (67,5%)** y **3.710/6.986 hojas completadas
  (53,1%)**, frente al 66,1% y 50,0% de v0.3.3.
- Actualizado el SDK de .NET a 10.0.303 (runtime 10.0.11), que corrige la vulnerabilidad de
  denegación de servicio CVE-2026-62901 del runtime con el que se compilan los ejecutables.

## v0.3.3

- Actualizado el corpus con **17.648 líneas nuevas aprobadas** y **545 hojas completadas**. El
  progreso global alcanza **534.529/808.960 líneas traducibles (66,1 %)** y
  **3.490/6.986 hojas completadas (50,0 %)**.
- Gran avance de *Heavensward*: pasa del **18,9 % al 78,5 %**, con
  **19.907/25.274 líneas traducidas**.
- Traducidos amplios bloques de la historia principal de *Heavensward* (`HeaVna` y `HeaVnz`),
  incluido el avance parcial del lote actual hasta `HeaVnz913`.
- Traducidas las cadenas de misiones tribales de los **Vanu Vanu**, los **gnath** y los
  **moguris** de *Heavensward*.
- Completado el lote de subtítulos de cinemáticas de *Heavensward* iniciado en v0.3.1:
  **17 hojas y 2.536 filas** de `VoiceMan 03000-03501`, ya sin filas pendientes de revisión.
- Añadidos diálogos regionales y de NPC de Ishgard, Coerthas, Dravania, Idíllshire, Mar de Nubes
  y la Diadema, además de cazas, Firmamento, Bozja, trovadores errantes, entregas personalizadas e
  Isla Santuario.
- Nuevos barridos de calidad y coherencia del corpus: tratamientos de personajes, género,
  terminología, puntuación y payloads SeString revisados junto con cada lote.

## v0.3.2

- Corregido el crash al abrir «Cambiar de Mundo» desde un aetheryte. El serializador SeString ahora
  usa la codificación extendida correcta cuando un cuerpo macro o run supera 206 bytes, sin acortar
  ni omitir la traducción española del panel.

## v0.3.1

- Revisión exhaustiva trilingüe (inglés, francés y alemán) de las **105.113 filas aprobadas** de
  misiones y diálogos: **314 lotes completados y 9.628 correcciones directas**, además de barridos
  globales de coherencia, gramática, género, puntuación y terminología.
- Primeros bloques de cinemáticas de *Heavensward* traducidos (`VoiceMan 03000-03300`): el progreso
  de la expansión sube del **11,4 % al 18,9 %**.
- Sustituida la compresión Brotli del corpus por Zstandard y restaurada su distribución embebida
  dentro del ejecutable single-file en Windows; ya no es necesario distribuir `translations.dat`
  por separado.
- La versión de Windows vuelve a compilarse y empaquetarse en un runner nativo de Windows.
  También se ha desactivado la compresión interna del bundle single-file para mitigar falsos
  positivos antivirus.

## v0.3.0

- ¡Nuevo hito alcanzado! ¡TODO el contenido de A Realm Reborn está totalmente traducido! 🥳🥳🥳
- ¡Nueva imagen! ¡Nuevo Patcher! 🎉
- Todo el nuevo contenido de la version 7.55 localizado en prioridad (incluido el futuro evento de FF7 en el Gold Saucer)
- Actualizado fichero README.md para reflejar estado del avance de la traducción basado en expansiones.

Ahora en adelante, empezaremos a localizar el contenido de Heavensward ⚔️

## v0.2.5

- Hotfix que impedia funcionar la aplicación en su ultima version
- Upgrade de la versión de Avalonia
- Validación de los ejecutables en el workflow CI para evitar futuras regresiones
- Sin cambios en la traducción con respecto a la anterior version

## v0.2.4

- Actualizado el corpus de traducciones embebido con **16.032 líneas nuevas aprobadas**. El progreso total alcanza **489.994/802.221 líneas traducibles (61,1 %)**, frente al 59,1 % de v0.2.3.
- Gran ampliación de misiones y diálogos de *A Realm Reborn*:
  - Más tramos de la historia principal de ARR
  - Nuevas misiones secundarias de las cadenas de quests de ARR
  - Misiones de las tribus bestia amalj'aa, sahagin e ixal
  - Misiones de job de *Heavensward* de astrólogo, caballero oscuro, monje, paladín e invocador, además del arco de las armas reliquia.
  - Más contenido de eventos estacionales y textos de Exploración Cósmica.
- Terminología revisada en todo el corpus: **Velo / Velo Negro** pasa a ser **Espesura / Espesura Negra**, con sus variantes regionales y referencias relacionadas actualizadas de forma consistente.
- Actualizadas dependencias y herramientas de desarrollo: SDK de .NET 10.0.302, Lumina 7.6.0, Lumina.Excel 7.5.0, Tmds.DBus.Protocol y Microsoft.NET.Test.Sdk. Añadida comprobación semanal de actualizaciones con Dependabot.

## v0.2.3

- Flexión de género del personaje: el patcher ahora genera condicionales de género nativos del juego, de forma que los diálogos concuerdan con el género del personaje del jugador (Guerrero/Guerrera de la Luz, aventurero/aventurera...).
- Actualizado el corpus de traducciones embebido (~47.000 líneas nuevas aprobadas):
  - Misiones de gremio post-Heavensward de TODAS las clases de crafteo y recolección: Alquimista, Minero, Carpintero, Curtidor, Armero, Pescador, Culinario, Botánico y Herrero (~90 hojas).
  - Historia principal ARR: parches 2.1 a 2.3 (bloques `GaiUse211-216` y lote `msq22`: apertura, Ul'dah y refugiados de Doma).
  - Lore y progreso: `MJIProgress` (Isla del Vagabundo), `WKSCosmoToolName` y arranque de `WKSMechaEventData` (Cosmic Exploration).
- Progreso total: 473.962/802.280 líneas (59,1%, antes 53,0%). Crafting/recolección al 74,2%, minijuegos/eventos al 99,8%, lore/diarios y eventos explícitos al 100%.
- Barrido tipográfico de todo el corpus: guiones de caja (U+2500) sustituidos por rayas (—) según la puntuación española.
- Ronda de canon ratificada por entrevista: decisiones de terminología y estilo aplicadas de forma coherente a todo el corpus.
- Correcciones de parcheo (vendor): auto-corrección de etiquetas de campo permutadas en `ExdPatcher` — filas que antes fallaban en silencio (biografías del Bozja Notebook, `Perform`, `Snipe`, `QTE`...) ahora se aplican al 100%.
- Arreglado falso positivo en la verificación del patcher.

## v0.2.2

- Actualizado el corpus de traducciones embebido con 8 lotes nuevos (~6.300 filas aprobadas, +16.769 líneas exactas):
  - Misiones de gremio y clase: Armero, Herrero, Culinario, Conjurador y Gladiador (29 hojas).
  - Misiones GaiUsa (`GaiUsa101-308`, 26 hojas).
  - Misiones de la Bandada de Syldra (`BanSyl005-301`, 22 hojas).
  - Eventos de temporada (años pasados): Starlight, All Saints' Wake, Moonfire Faire, Valentione, Hatching-tide, Little Ladies' Day y Gold Saucer (18 hojas).
  - Guías de raid y duty (12 hojas).
  - Tiendas y teleports (24 hojas).
  - Gimmicks: `GimmickBill`, `GimmickYesNo`, Halloween y pesca oceánica.
  - Textos custom: guías de principiante, avisos PvP, pistas de Notorious Monsters, Unukalhai, Tríada Bélica y diálogos regionales (33 hojas; dominio custom completo, 82/82 ficheros).
- Progreso total: 426.936/804.955 líneas (53,0%, antes 50,7%). Combate/duties al 99,9%, Mundo/NPCs al 100%, eventos explícitos al 98,5%.
- Coherencia de terminología: auditoría y canon-lint en todos los lotes
- Página de Nexus Mods actualizada: enlace de release y versión de Linux publicados.

## v0.2.1

- Actualizado el corpus de traducciones embebido (`translations.dat`), regenerado por primera vez desde Linux.
- Quitado fléxión de género con @ en toda la traducción. (muy artificial y forzado)
- ~9.900 líneas nuevas traducidas y aprobadas:
  - Revisión de calidad de traducción de la v0.2.0
  - Nuevas misiones de historia principal ARR (bloques `ManFst006-304` y `GaiUse606-616`).
  - Mazmorras profundas (Deep Dungeon) traducidas.
  - FATEs (`FateEvent`, primer lote).
  - TODAS las misiones de Hildibrand (Dawntrail incluido)
  - Acciones y combate: `ActionComboRoute`, `AozActionTransient`, `BgcArmyAction`, `AOZScore` y atributos base (`BaseParam`).
  - Textos del buscador de contenidos (`ContentFinderConditionTransient`).
- Correcciones de calidad en el corpus: filas no localizables depuradas y arreglo de traducciones que partían por error del francés/alemán.
- Mejorada la detección automática de la ruta del juego, especialmente en Linux

## v0.2.0

- README.md ahora informa del avance actual de la traducción
- Gran ampliación de contenido de misiones y diálogos (15.901 lineas traducidas):
  - 146 nuevas sheets `quest/` traducidas respecto al corte anterior, excluyendo los commits de extracción masiva.
  - Nuevas líneas de historia principal ARR, incluyendo bloques `GaiUse`, `GaiUsd`, `ManFst`, `ManSea` y `ManWil`.
  - Nuevas misiones de clase para arquero, conjurador, lancero y pícaro.
  - Nuevas misiones tribales Amalj'aa y sílfides.
  - Nuevas misiones estacionales: Halloween, Heavensturn, Little Ladies' Day y eventos relacionados.
  - Nuevas misiones de Hildibrand/Manderville (`ChrHdb`).
- Ampliado el soporte del patcher para familias completas de sheets de guion:
  - `quest/`, `custom/`, `content/`, `cut_scene/`, `dungeon/`, `guild_order/`, `leve/` y `opening/` ahora se agrupan automáticamente en la categoría `misiones`.
- Añadidas más sheets de contenido instanciado y scripts de sistema:
  - Deep Dungeon / Deep Dungeon 2, Halloween entrance, entradas de dungeon, guild orders, leves, openings y NPCs/servicios comunes.
- Validado que las 549 sheets empaquetables del corpus local quedan cubiertas por categorías conocidas.

## v0.1.3

- Añadidas nuevas sheets traducidas al listado de categorías del patcher.
- Revisado lote de traducción de la v0.1.0
- Arreglada gramática chunga de la traducción de la mayoria de los emotes. (aun se puede mejorar)
- 9987 lineas nuevas traducidas
- Nuevas sheets de `misiones`:
  - `custom/000/ComDefFreeCompany_00076`
  - `custom/000/ComDefGCSupplyDuty_00075`
  - `custom/000/ComDefGrandCompany_00046`
  - `custom/000/ComDefGrandCompanyOfficer_00073`
  - `custom/000/ComDefSanction_00086`
  - `custom/000/RegFstAdvGuild_00005`
  - `custom/000/RegFstAetheryteGuid_00032`
  - `custom/000/RegFstArcGuild_00008`
  - `custom/000/RegFstCnjGuild_00023`
  - `custom/000/RegFstCnjPreach_00024`
  - `custom/000/RegFstEternalCeremonyGuideHall_00017`
  - `custom/000/RegFstEternalCeremonyGuideRoom_00016`
  - `custom/000/RegFstHrvGuild_00033`
  - `custom/000/RegFstInnInfo_00022`
  - `custom/000/RegFstLncGuild_00007`
  - `custom/000/RegFstMagicItemTips_00045`
  - `custom/000/RegFstTanGuild_00030`
  - `custom/000/RegFstWdkGuild_00029`
  - `custom/000/RegSeaAcnGuild_00089`
  - `custom/000/RegSeaAdvGuild_00050`
  - `custom/000/RegSeaAetheGuid_00051`
  - `custom/000/RegSeaArmGuild_00056`
  - `custom/000/TstPlnCmpFCCounter_00035`
  - `custom/000/TstPrgTest_00001`
  - `custom/001/ComDefFreeCompanyCrest_00101`
  - `custom/001/ComDefFreeCompanyReward_00100`
  - `custom/001/ComDefFrontLine_00182`
  - `custom/001/ComDefHousingOfficer_00136`
  - `custom/001/ComDefMobOfficer_00180`
  - `custom/001/ComDefSuspendedMateria_00103`
  - `custom/002/ComDefMobHuntBoard_00202`
  - `custom/003/ComArmGcArmyEnterLobby_00325`
  - `custom/003/ComArmGcArmyInterview_00345`
  - `custom/003/ComArmGcArmyOfficer_00342`
  - `custom/003/ComArmGcArmyTraining_00344`
  - `cut_scene/022/VoiceMan_02200`
  - `cut_scene/023/VoiceMan_02300`
  - `cut_scene/024/VoiceMan_02400`
  - `cut_scene/024/VoiceMan_02401`
  - `cut_scene/025/VoiceMan_02500`
  - `opening/OpeningGridania`
  - `opening/OpeningLimsaLominsa`
  - `opening/OpeningUldah`
  - `quest/000/GaiUsd020_00090`
  - `quest/000/GaiUsd501_00043`
  - `quest/000/GaiUsd502_00044`
  - `quest/000/GaiUse401_00052`
  - `quest/000/GaiUse402_00053`
  - `quest/000/GaiUse415_00084`
  - `quest/000/ManFst000_00083`
  - `quest/004/ManFst005_00445`
  - `quest/005/ManFst306_00514`
  - `quest/005/ManFst405_00520`
  - `quest/005/ManFst503_00524`
  - `quest/005/ManSea000_00541`
  - `quest/005/ManSea005_00543`
  - `quest/005/ManWil000_00548`
  - `quest/005/ManWil005_00550`
- Nuevas sheets de `interfaz`:
  - `ChatBubbleType`
  - `CircleActivity`
  - `TopicSelect`
- Nuevas sheets de `items`:
  - `AquariumWater`
  - `BankaCraftWorks`
  - `CabinetSubCategory`
  - `ChocoboRaceItem`
  - `CollectablesShop`
  - `CollectablesShopItemGroup`
  - `CompanyCraftDraft`
  - `CompanyCraftDraftCategory`
  - `CompanyCraftManufactoryState`
  - `CompanyCraftType`
  - `CraftLeveTalk`
  - `CraftType`
  - `DisposalShop`
  - `DisposalShopFilterType`
  - `EurekaAetherItem`
  - `EurekaMagiciteItemType`
  - `FccShop`
  - `FittingShopCategory`
  - `FittingShopItemSet`
  - `GCShopItemCategory`
  - `GilShop`
  - `GlassesStyle`
  - `HousingAppeal`
  - `HousingEmploymentNpcRace`
  - `HousingMateAuthority`
  - `HousingMerchantPose`
  - `HousingPlacement`
  - `HousingPreset`
  - `HousingRenovation`
  - `HousingUnplacement`
  - `HugeCraftworksNpc`
  - `HWDCrafterSupplyTerm`
  - `InclusionShop`
  - `InclusionShopCategory`
  - `InclusionShopWelcomText`
  - `LotteryExchangeShop`
  - `MJICraftworksObjectTheme`
  - `MJIItemCategory`
  - `RecipeSubCategory`
  - `SharlayanCraftWorks`
  - `SpecialShop`
  - `Stain`
  - `TomestoneConvert`
  - `TofuBg`
  - `TofuEditParam`
  - `TofuObject`
  - `TofuObjectCategory`
  - `TofuPreset`
  - `TofuPresetCategory`
  - `Treasure`
  - `ValentionSweetsMaterial`
  - `ValentionSweetsRecipe`
  - `Warp`
  - `WKSItemSubCategory`
  - `YKW`
- Nuevas sheets de `logros`:
  - `AchievementKind`
  - `ContentsNote`
  - `ContentsNoteCategory`
  - `Description`
  - `DescriptionStandAloneTransient`
  - `VVDVoteRouteLabel`
- Nueva sheet de `nombres`:
  - `BeastReputationRank`
- Nueva sheet de `eventos`:
  - `EventItemCategory`
- Nueva sheet de `coleccionables`:
  - `OrchestrionCategory`

## v0.1.1

- Añadida sección de resolución de problemas frecuentes en README.md
- Arreglada release y falso positivo de virustotal para release de NexusMods.

## v0.1.0

- Añadido CHANGELOG.md
- Añadido Aviso Legal
- Deprecado soporte para Macs antiguos
- Añadida automatizacion de publicación de release en Nexus Mods
- Metadatos enriquecidos del mod (`meta.json` del `.pmp`):
- La consola de la aplicación ahora permite seleccionar texto con el ratón y copiarlo con Ctrl+C.
- Correcciones de parcheo (vendor): alias posicionales `Column{i}` en `ExdPatcher` y resolución de campos SeString en miembros de colección.
- Añadidas nuevas sheets traducidas al listado de categorías del patcher para que aparezcan bajo los toggles existentes del panel avanzado.
- 8271 lineas nuevas traducidas
- Nuevas sheets de `misiones`:
  - `AirshipExplorationLog`
  - `AnimaWeaponFUITalkParam`
  - `CompleteJournal`
  - `ContentUICategory`
  - `DescriptionString`
  - `InstanceContentTextData`
- Nuevas sheets de `interfaz`:
  - `ContentsTutorialPage`
  - `EmjAddon`
  - `EventTutorial`
  - `EventTutorialPage`
  - `FGSAddon`
  - `FieldMarker`
  - `FurnitureCatalogCategory`
  - `GuideTitle`
  - `GuildleveAssignment`
  - `GuildleveAssignmentTalk`
  - `GuildOrder`
  - `HWDDevLevelWebText`
  - `Marker`
  - `McGuffinUIData`
  - `MJIDisposalShopUICategory`
  - `MJIHudMode`
  - `MYCTemporaryItemUICategory`
  - `OmikujiGuidance`
  - `PerformGuideScore`
  - `Platform`
  - `QuestRedoChapterUI`
  - `QuestRedoChapterUICategory`
  - `QuestRedoChapterUITab`
  - `QuickChatTransient`
  - `SpearfishingEcology`
  - `SubmarineExplorationLog`
  - `TextCommandParam`
  - `WarpLogic`
  - `WebGuidance`
  - `WKSNextPlanetGuidance`
  - `WKSPraiseUI`
  - `YardCatalogCategory`
- Nueva sheet de `clases`:
  - `ClassJobActionUICategory`
- Nuevas sheets de `items`:
  - `BuddyEquip`
  - `DeepDungeonEquipment`
  - `Glasses`
- Nueva sheet de `nombres`:
  - `DawnMemberUIParam`
- Nueva sheet de `coleccionables`:
  - `CompanionTransient`
