package dto

type IdentityCli struct {
	Name              string
	FingerPrint       string
	Active            bool
	Config            string
	ControllerVersion string
	Status            string
}

type ServiceCli struct {
	Name          string
	AssignedIP    string
	InterceptHost string
	InterceptPort uint16
	Id            string
	AssignedHost  string
	OwnsIntercept bool
}
