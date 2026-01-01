set serviceName=WheelDiverterSorter.Host

sc stop   %serviceName% 
sc delete %serviceName% 

pause