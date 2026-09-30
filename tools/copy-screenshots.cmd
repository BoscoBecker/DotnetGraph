@echo off
setlocal
set "SRC=C:\Users\jbosco\.cursor\projects\d-DotnetGraph\assets"
set "DST=d:\DotnetGraph\docs\screenshots"
if not exist "%DST%" mkdir "%DST%"
set "PREFIX=c__Users_jbosco_AppData_Roaming_Cursor_User_workspaceStorage_0e5d3703e0c1d9db2d64892270b11788_images_image-"
copy /Y "%SRC%\%PREFIX%a822c315-0e97-4a2e-9526-f6ac1f72f494.png" "%DST%\architecture.png"
copy /Y "%SRC%\%PREFIX%2a5bc030-20c9-45b2-9e2b-04c900c1afee.png" "%DST%\architecture-detail.png"
copy /Y "%SRC%\%PREFIX%4ba7be91-7701-441d-acd6-c4d937d72946.png" "%DST%\call-graph.png"
copy /Y "%SRC%\%PREFIX%42b7c7b1-adcd-46b1-8246-ae66469df662.png" "%DST%\namespaces.png"
copy /Y "%SRC%\%PREFIX%78e7882d-0907-4bac-b0a9-7d883b85103c.png" "%DST%\types.png"
dir "%DST%\*.png"
